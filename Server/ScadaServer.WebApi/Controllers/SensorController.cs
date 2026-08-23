using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SensorController : ControllerBase
    {
        private readonly ISensorAppService _appService;

        public SensorController(ISensorAppService appService)
        {
            _appService = appService;
        }

        [HttpGet("device/{deviceId}")]
        public async Task<IActionResult> GetByDevice(int deviceId)
        {
            var list = await _appService.GetListAsync();
            return Ok(list.Where(s => s.DeviceId == deviceId));
        }

        [HttpPost]
        public async Task<IActionResult> AddSensor([FromBody] SensorDto dto)
        {
            await _appService.CreateAsync(dto);
            return Ok(dto);
        }
    }
}
