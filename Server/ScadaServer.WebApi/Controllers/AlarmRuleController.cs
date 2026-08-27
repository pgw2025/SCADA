using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Runtime.Alarms;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 报警规则控制器：规则配置 CRUD（管理员写，认证用户读）。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AlarmRuleController : ControllerBase
    {
        private readonly IAlarmRuleAppService _appService;
        private readonly IAlarmRuleEngine _ruleEngine;

        public AlarmRuleController(IAlarmRuleAppService appService, IAlarmRuleEngine ruleEngine)
        {
            _appService = appService;
            _ruleEngine = ruleEngine;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var dto = await _appService.GetByIdAsync(id);
            return dto == null ? NotFound() : Ok(dto);
        }

        [Authorize(Policy = "RequireAdmin")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AlarmRuleDto dto)
        {
            await _appService.CreateAsync(dto);
            // 规则变更后热重载运行时告警引擎，使新规则立即生效（无需等待 30s 周期刷新）
            await _ruleEngine.ReloadAsync();
            return Ok(dto);
        }

        [Authorize(Policy = "RequireAdmin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] AlarmRuleDto dto)
        {
            dto.Id = id;
            await _appService.UpdateAsync(dto);
            await _ruleEngine.ReloadAsync();
            return Ok(dto);
        }

        [Authorize(Policy = "RequireAdmin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            await _ruleEngine.ReloadAsync();
            return Ok();
        }

        [Authorize(Policy = "RequireAdmin")]
        [HttpPut("{id}/toggle")]
        public async Task<IActionResult> Toggle(int id, [FromBody] bool enabled)
        {
            var dto = await _appService.GetByIdAsync(id);
            if (dto != null)
            {
                dto.Active = enabled;
                await _appService.UpdateAsync(dto);
                await _ruleEngine.ReloadAsync();
            }
            return Ok();
        }
    }
}