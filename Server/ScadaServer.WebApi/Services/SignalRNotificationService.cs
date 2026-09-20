using System.Threading;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Enums;
using ScadaServer.WebApi.Hubs;

namespace ScadaServer.WebApi.Services
{
    /// <summary>
    /// SignalR通知服务实现，同时支持MQTT发布
    /// </summary>
    /// <remarks>
    /// 仅作为 IScadaNotificationService 的下游实现，被 RuntimeManager 主动调用。
    /// 不再注入 IRuntimeManager，避免与 RuntimeManager 注入 IScadaNotificationService 形成 Singleton 循环依赖。
    /// 设备状态变更推送由 RuntimeManager.OnDeviceConnectionStateChanged 主动调用 NotifyDeviceStatusAsync 完成。
    /// </remarks>
    public class SignalRNotificationService : IScadaNotificationService, IAsyncDisposable
    {
        /// <summary>MQTT 发布队列容量：满则丢最旧（DropOldest），避免发布慢时反向堆积。</summary>
        private const int MqttQueueCapacity = 4096;

        private readonly IHubContext<ScadaHub> _hubContext;
        private readonly IMqttManager _mqttManager;
        private readonly ILogger<SignalRNotificationService> _logger;

        // MQTT 发布解耦为独立有界队列 + 后台单消费者：通知泵不再被 MQTT 网络 IO 阻塞，
        // 高频变化设备也不会因 MQTT 抖动触发通知通道 DropOldest 丢消息。
        private readonly Channel<(int DeviceId, string VariableKey, object Value)> _mqttQueue =
            Channel.CreateBounded<(int, string, object)>(new BoundedChannelOptions(MqttQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        private readonly CancellationTokenSource _mqttCts = new();
        private readonly Task _mqttPump;

        /// <summary>
        /// 初始化通知服务
        /// </summary>
        /// <param name="hubContext">SignalR Hub上下文</param>
        /// <param name="mqttManager">MQTT管理器</param>
        /// <param name="logger">日志记录器</param>
        public SignalRNotificationService(
            IHubContext<ScadaHub> hubContext,
            IMqttManager mqttManager,
            ILogger<SignalRNotificationService> logger)
        {
            _hubContext = hubContext;
            _mqttManager = mqttManager;
            _logger = logger;
            _mqttPump = Task.Run(() => PumpMqttAsync(_mqttCts.Token));
        }

        /// <inheritdoc/>
        public async Task NotifyVariableUpdateAsync(int deviceId, string variableKey, object? value, VariableQuality quality, DateTime updateTime)
        {
            // SignalR通知：结构化载荷（值 + 质量 + 采集时间 UTC）仅推送至订阅该设备的分组。
            // 携带质量与采集时间使前端能区分"真实采集值"与"读取失败后的僵尸值"，
            // 并以采集时刻（而非浏览器接收时刻）作为更新时间展示。
            await _hubContext.Clients
                .Group(ScadaHub.DeviceGroup(deviceId))
                .SendAsync("ReceiveVariableUpdate", new
                {
                    DeviceId = deviceId,
                    VariableKey = variableKey,
                    Value = value,
                    Quality = quality.ToString(),
                    UpdateTime = updateTime
                });

            // MQTT通知：非阻塞入队（质量降级且无有效值时不发布），由独立后台泵串行发布。
            var mqttValue = value;
            if (mqttValue != null)
            {
                _mqttQueue.Writer.TryWrite((deviceId, variableKey, mqttValue));
            }
        }

        /// <inheritdoc/>
        public async Task NotifyDeviceStatusAsync(int deviceId, DeviceStatus status)
        {
            // SignalR通知：向所有连接的客户端广播设备状态变更。
            // 显式传枚举名（status.ToString()），SignalR 默认 JSON 协议会把枚举序列化为数字，
            // 与 REST 接口的字符串枚举不一致会导致前端状态映射错位（设备恒显离线）。
            await _hubContext.Clients.All.SendAsync("ReceiveDeviceStatus", deviceId, status.ToString());

            // MQTT通知：发布设备状态到MQTT服务器（当前 MQTT 管理器未实现状态发布，静默忽略）
        }

        /// <inheritdoc/>
        public async Task NotifySystemAlarmAsync(int deviceId, string variableKey, string variableName, string message, string level)
        {
            // SignalR通知：向所有连接的客户端广播系统报警（前端 ReceiveSystemAlarm 监听展示）。
            // MQTT通知：当前 MQTT 管理器未实现报警发布，静默忽略。
            await _hubContext.Clients.All.SendAsync("ReceiveSystemAlarm", deviceId, variableKey, variableName, message, level);
        }

        /// <inheritdoc/>
        public async Task NotifyAlarmAsync(AlarmEvent evt)
        {
            // 结构化报警事件推送：整对象序列化（SignalR 默认协议会把枚举序列化为数字，
            // 与 REST 接口的字符串枚举不一致，此处显式按对象推送，前端据此做列表/角标/确认态）。
            await _hubContext.Clients.All.SendAsync("ReceiveAlarm", evt);
        }

        /// <inheritdoc/>
        public async Task NotifyScriptExecutionAsync(ScriptExecutionEvent evt)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveScriptExecution", evt);
        }

        /// <summary>
        /// MQTT 发布后台消费循环：串行逐条发布，单条失败仅记 Debug 不中断泵；
        /// 通道完成并排空后任务退出。
        /// </summary>
        private async Task PumpMqttAsync(CancellationToken token)
        {
            try
            {
                await foreach (var (deviceId, variableKey, value) in _mqttQueue.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        await _mqttManager.PublishVariableUpdateAsync(deviceId, variableKey, value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "MQTT 发布变量 {Key} 失败（设备 #{DeviceId}）。", variableKey, deviceId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出路径
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT 发布泵因未预期异常退出。");
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            _mqttQueue.Writer.TryComplete();
            _mqttCts.Cancel();
            try
            {
                await _mqttPump;
            }
            catch (OperationCanceledException)
            {
                // 忽略停止取消
            }
            _mqttCts.Dispose();
        }
    }
}
