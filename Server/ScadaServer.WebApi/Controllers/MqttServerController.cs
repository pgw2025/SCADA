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

        /// <summary>
        /// 启用/停用服务器（停用即断开连接且不再发布）。
        /// </summary>
        [HttpPut("{id}/enabled")]
        [AuditLog("MQTT服务器", "SET_ENABLED")]
        public async Task<IActionResult> SetEnabled(int id, [FromQuery] bool enabled)
        {
            await _appService.SetEnabledAsync(id, enabled);
            return Ok(await _appService.GetByIdAsync(id));
        }

        /// <summary>
        /// 返回所有服务器的实时连接状态（供前端卡片状态展示）。
        /// </summary>
        [HttpGet("statuses")]
        public async Task<IActionResult> GetStatuses() => Ok(await _appService.GetStatusesAsync());

        /// <summary>
        /// 使用给定参数测试连接（不落库、不影响现有连接）。
        /// </summary>
        [HttpPost("test")]
        [AuditLog("MQTT服务器", "TEST")]
        public async Task<IActionResult> TestConnection([FromBody] MqttServerDto dto)
            => Ok(await _appService.TestConnectionAsync(dto));
    }
}