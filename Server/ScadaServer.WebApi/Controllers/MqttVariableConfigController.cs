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
    public class MqttVariableConfigController : ControllerBase
    {
        private readonly IMqttVariableConfigAppService _appService;

        public MqttVariableConfigController(IMqttVariableConfigAppService appService)
        {
            _appService = appService;
        }

        /// <summary>
        /// 查询指定服务器下所有关联变量（含设备名、变量名、主题预览、实时值）。
        /// </summary>
        [HttpGet("{serverId:int}/variables")]
        public async Task<IActionResult> GetByServer(int serverId)
            => Ok(await _appService.GetByServerAsync(serverId));

        /// <summary>
        /// 为服务器新增关联变量（同一服务器下同一设备同一变量唯一）。
        /// </summary>
        [HttpPost("{serverId:int}/variables")]
        [AuditLog("MQTT变量映射", "CREATE")]
        public async Task<IActionResult> Add(int serverId, [FromBody] MqttVariableConfigCreateDto dto)
        {
            var created = await _appService.AddAsync(serverId, dto);
            return Ok(created);
        }

        /// <summary>
        /// 更新关联变量（别名/自定义主题/启用开关）。
        /// </summary>
        [HttpPut("variables/{configId:int}")]
        [AuditLog("MQTT变量映射", "UPDATE")]
        public async Task<IActionResult> Update(int configId, [FromBody] MqttVariableConfigUpdateDto dto)
        {
            var updated = await _appService.UpdateAsync(configId, dto);
            if (updated == null) return NotFound();
            return Ok(updated);
        }

        /// <summary>
        /// 删除关联变量。
        /// </summary>
        [HttpDelete("variables/{configId:int}")]
        [AuditLog("MQTT变量映射", "DELETE")]
        public async Task<IActionResult> Delete(int configId)
        {
            await _appService.DeleteAsync(configId);
            return Ok();
        }
    }
}