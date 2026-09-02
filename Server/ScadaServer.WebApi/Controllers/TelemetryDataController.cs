using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TelemetryDataController : ApiControllerBase
    {
        private readonly IMqttService _mqttService;
        private readonly RuntimeManager _runtimeManager;

        public TelemetryDataController(IMqttService mqttService, RuntimeManager runtimeManager)
        {
            _mqttService = mqttService;
            _runtimeManager = runtimeManager;
        }

        [HttpGet("{deviceId}/realtime")]
        public IActionResult GetRealtime(int deviceId)
        {
            if (!_runtimeManager.DeviceRuntimes.TryGetValue(deviceId, out var runtime))
            {
                return NotFound(new { DeviceId = deviceId, Message = "设备未运行或未启用" });
            }

            var variables = runtime.Variables.Values
                .Select(v => new
                {
                    Key = v.Key,
                    Name = v.Name,
                    Value = v.Value,
                    Quality = v.Quality.ToString(),
                    UpdateTime = v.UpdateTime
                })
                .ToList();

            return Ok(new
            {
                DeviceId = deviceId,
                DeviceKey = runtime.Device.Key,
                Variables = variables,
                // 项目时间戳约定：统一 UTC（前端本地化展示）
                Timestamp = DateTime.UtcNow
            });
        }

        [HttpPost("publish-manual")]
        public async Task<IActionResult> ManualPublish([FromBody] string message)
        {
            // 守卫裸字符串参数为空（JSON null / 空串 / 纯空白）：避免发布空载荷。
            var err = EnsureNotBlank(message, "message", "发布消息内容不能为空");
            if (err != null) return err;

            await _mqttService.PublishAsync("telemetry/manual", message);
            return Ok("Published to MQTT");
        }
    }
}
