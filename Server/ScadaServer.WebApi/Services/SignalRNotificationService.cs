using Microsoft.AspNetCore.SignalR;
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
    public class SignalRNotificationService : IScadaNotificationService
    {
        private readonly IHubContext<ScadaHub> _hubContext;
        private readonly IMqttManager _mqttManager;

        /// <summary>
        /// 初始化通知服务
        /// </summary>
        /// <param name="hubContext">SignalR Hub上下文</param>
        /// <param name="mqttManager">MQTT管理器</param>
        public SignalRNotificationService(
            IHubContext<ScadaHub> hubContext,
            IMqttManager mqttManager)
        {
            _hubContext = hubContext;
            _mqttManager = mqttManager;
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
            // SignalR通知：向所有连接的客户端广播设备状态变更。
            // 显式传枚举名（status.ToString()），SignalR 默认 JSON 协议会把枚举序列化为数字，
            // 与 REST 接口的字符串枚举不一致会导致前端状态映射错位（设备恒显离线）。
            await _hubContext.Clients.All.SendAsync("ReceiveDeviceStatus", deviceId, status.ToString());

            // MQTT通知：发布设备状态到MQTT服务器（当前 MQTT 管理器未实现状态发布，静默忽略）
        }
    }
}
