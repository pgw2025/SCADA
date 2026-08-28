using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Runtime.Scripting;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class SystemScriptController : ControllerBase
    {
        private readonly ISystemScriptAppService _appService;
        private readonly IScriptValidationService _validationService;
        private readonly IScriptExecutionRecordRepository _recordRepo;
        private readonly IScriptEngineHost _scriptEngine;

        public SystemScriptController(
            ISystemScriptAppService appService,
            IScriptValidationService validationService,
            IScriptExecutionRecordRepository recordRepo,
            IScriptEngineHost scriptEngine)
        {
            _appService = appService;
            _validationService = validationService;
            _recordRepo = recordRepo;
            _scriptEngine = scriptEngine;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _appService.GetByIdAsync(id));

        /// <summary>
        /// 按脚本分页查询执行记录（控制台追溯）。返回 { total, items }。
        /// </summary>
        [HttpGet("{id}/records")]
        public async Task<IActionResult> GetRecords(int id, [FromQuery] string? result, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            var (total, items) = await _recordRepo.QueryByScriptAsync(id, result, pageIndex, pageSize);
            return Ok(new { total, items });
        }

        [HttpPost]
        [AuditLog("系统脚本", "CREATE")]
        public async Task<IActionResult> Create([FromBody] SystemScriptDto dto)
        {
            await _appService.CreateAsync(dto);
            await _scriptEngine.ReloadAsync();
            return Ok(dto);
        }

        [HttpPut]
        [AuditLog("系统脚本", "UPDATE")]
        public async Task<IActionResult> Update([FromBody] SystemScriptDto dto)
        {
            await _appService.UpdateAsync(dto);
            await _scriptEngine.ReloadAsync();
            return Ok();
        }

        [HttpDelete("{id}")]
        [AuditLog("系统脚本", "DELETE")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            await _scriptEngine.ReloadAsync();
            return Ok();
        }

        /// <summary>
        /// 静态校验脚本（元数据 + 代码语法），不落库、不执行。保存/试运行前由前端调用。
        /// </summary>
        [HttpPost("validate")]
        public IActionResult Validate([FromBody] SystemScriptDto dto)
            => Ok(_validationService.Validate(dto));

        /// <summary>
        /// 人工复位脚本熔断状态（Tripped=false、FailureCount=0）。
        /// </summary>
        [HttpPost("{id}/reset-tripped")]
        public async Task<IActionResult> ResetTripped(int id)
        {
            await _appService.ResetTrippedAsync(id);
            await _scriptEngine.ReloadAsync();
            return Ok();
        }

        /// <summary>
        /// 手动执行脚本（服务端沙箱执行）。返回本执行的结果（含 log 输出）。
        /// </summary>
        [HttpPost("{id}/run")]
        public async Task<IActionResult> Run(int id)
        {
            var executor = User.FindFirst(ClaimTypes.Name)?.Value
                           ?? User.FindFirst("username")?.Value
                           ?? User.Identity?.Name
                           ?? "unknown";
            var result = await _scriptEngine.RunAsync(id, executor);
            return Ok(result);
        }

        /// <summary>
        /// 试运行脚本（dry-run，不落库、不真实写入、不更新熔断态）。前端调试控制台使用。
        /// </summary>
        [HttpPost("test")]
        public async Task<IActionResult> Test([FromBody] ScriptTestRequestDto request)
        {
            var executor = User.FindFirst(ClaimTypes.Name)?.Value
                           ?? User.FindFirst("username")?.Value
                           ?? User.Identity?.Name
                           ?? "unknown";

            var entity = new SystemScript
            {
                Id = 0,
                Name = request.Script.Name,
                Code = request.Script.Code,
                TriggerType = request.Script.TriggerType,
                IntervalSeconds = request.Script.IntervalSeconds,
                CronExpression = request.Script.CronExpression,
                WatchDeviceKey = request.Script.WatchDeviceKey,
                WatchVariableKey = request.Script.WatchVariableKey,
                DeadBand = request.Script.DeadBand,
                CooldownMs = request.Script.CooldownMs,
                TimeoutMs = request.Script.TimeoutMs,
                ScopeRead = request.Script.ScopeRead,
                ScopeWrite = request.Script.ScopeWrite,
                Active = request.Script.Active,
                Version = 0
            };

            var result = await _scriptEngine.TestAsync(entity, request.DeviceKey, request.VariableKey, executor);
            return Ok(result);
        }
    }
}
