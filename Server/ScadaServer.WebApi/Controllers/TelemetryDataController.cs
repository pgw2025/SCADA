using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TelemetryDataController : ControllerBase
    {
        private readonly IMqttService _mqttService;

        public TelemetryDataController(IMqttService mqttService)
        {
            _mqttService = mqttService;
        }

        [HttpGet("{deviceId}/realtime")]
        public IActionResult GetRealtime(int deviceId)
        {
            // In real app, this might pull from Redis or a memory cache updated by DeviceWorker
            return Ok(new { DeviceId = deviceId, Value = 88.5, Unit = "°C", Timestamp = DateTime.Now });
        }

        [HttpPost("publish-manual")]
        public async Task<IActionResult> ManualPublish([FromBody] string message)
        {
            await _mqttService.PublishAsync("telemetry/manual", message);
            return Ok("Published to MQTT");
        }
    }
}
