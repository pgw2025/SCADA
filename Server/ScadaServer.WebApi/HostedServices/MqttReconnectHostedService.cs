using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// MQTT 断线重连后台服务：周期性扫描所有「已启用但未连接」的 MQTT 服务器并触发重连，
    /// 由 MqttManager 内部记录状态与错误信息，供前端状态卡片展示。
    /// </summary>
    public class MqttReconnectHostedService : BackgroundService
    {
        private readonly IMqttManager _mqttManager;
        private readonly ILogger<MqttReconnectHostedService> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

        public MqttReconnectHostedService(IMqttManager mqttManager, ILogger<MqttReconnectHostedService> logger)
        {
            _mqttManager = mqttManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                    await _mqttManager.ReconnectAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MQTT 重连扫描异常，下一轮继续。");
                }
            }
        }
    }
}