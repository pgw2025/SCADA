using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class MqttServerController : ControllerBase
    {
        private readonly IMqttServerAppService _appService;

        public MqttServerController(IMqttServerAppService appService)
        {
            _appService = appService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _appService.GetByIdAsync(id));

        [HttpPost]
        [AuditLog("MQTT服务器", "CREATE")]
        public async Task<IActionResult> Create([FromBody] MqttServerDto dto)
        {
            await _appService.CreateAsync(dto);
            return Ok(dto);
        }

        [HttpPut]
        [AuditLog("MQTT服务器", "UPDATE")]
        public async Task<IActionResult> Update([FromBody] MqttServerDto dto)
        {
            await _appService.UpdateAsync(dto);
            return Ok();
        }

        [HttpDelete("{id}")]
        [AuditLog("MQTT服务器", "DELETE")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok();
        }
    }
}
