using Microsoft.AspNetCore.SignalR;
using ScadaServer.Application.Interfaces;
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
        public SignalRNotificationService(IHubContext<ScadaHub> hubContext, IMqttManager mqttManager)
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
    }
}
