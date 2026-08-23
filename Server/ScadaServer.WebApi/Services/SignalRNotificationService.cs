using Microsoft.AspNetCore.SignalR;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Interface;
using ScadaServer.WebApi.Hubs;

namespace ScadaServer.WebApi.Services
{
    /// <summary>
    /// SignalR通知服务实现，同时支持MQTT发布
    /// </summary>
    public class SignalRNotificationService : IScadaNotificationService
    {
        private readonly IHubContext<ScadaHub> _hubContext;
        private readonly IMqttManager _mqttManager;

        /// <summary>
        /// 初始化通知服务
        /// </summary>
        /// <param name="hubContext">SignalR Hub上下文</param>
        /// <param name="mqttManager">MQTT管理器</param>
        /// <param name="runtimeManager">运行时管理器（订阅状态变更事件）</param>
        public SignalRNotificationService(
            IHubContext<ScadaHub> hubContext,
            IMqttManager mqttManager,
            IRuntimeManager runtimeManager)
        {
            _hubContext = hubContext;
            _mqttManager = mqttManager;

            // 订阅运行时状态变更，向所有客户端实时推送设备上线/离线/故障。
            runtimeManager.StatusChanged += OnRuntimeStatusChanged;
        }

        private async void OnRuntimeStatusChanged(object? sender, DeviceStatusChangedEventArgs e)
        {
            try
            {
                await NotifyDeviceStatusAsync(e.DeviceId, e.Status);
            }
            catch (Exception ex)
            {
                // 推送失败不应影响运行时采集循环
                Console.Error.WriteLine($"[SignalR] 设备状态推送失败: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task NotifyVariableUpdateAsync(int deviceId, string variableKey, object value)
        {
            // SignalR通知：向所有连接的客户端广播变量更新
            await _hubContext.Clients.All.SendAsync("ReceiveVariableUpdate", deviceId, variableKey, value);

            // MQTT通知：发布变量更新到MQTT服务器
            await _mqttManager.PublishVariableUpdateAsync(deviceId, variableKey, value);
        }

        /// <inheritdoc/>
        public async Task NotifyDeviceStatusAsync(int deviceId, DeviceStatus status)
        {
            // SignalR通知：向所有连接的客户端广播设备状态变更
            await _hubContext.Clients.All.SendAsync("ReceiveDeviceStatus", deviceId, status);

            // MQTT通知：发布设备状态到MQTT服务器（当前 MQTT 管理器未实现状态发布，静默忽略）
        }
    }
}
