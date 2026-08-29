using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Runtime.Tasks;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class ScheduledTaskController : ControllerBase
    {
        private readonly IScheduledTaskAppService _appService;
        private readonly IScheduledTaskScheduler _scheduler;

        public ScheduledTaskController(
            IScheduledTaskAppService appService,
            IScheduledTaskScheduler scheduler)
        {
            _appService = appService;
            _scheduler = scheduler;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _appService.GetByIdAsync(id));

        [HttpPost]
        [AuditLog("定时任务", "CREATE")]
        public async Task<IActionResult> Create([FromBody] ScheduledTaskDto dto)
        {
            await _appService.CreateAsync(dto);
            await _scheduler.ReloadAsync();
            return Ok(dto);
        }

        [HttpPut]
        [AuditLog("定时任务", "UPDATE")]
        public async Task<IActionResult> Update([FromBody] ScheduledTaskDto dto)
        {
            await _appService.UpdateAsync(dto);
            await _scheduler.ReloadAsync();
            return Ok();
        }

        [HttpDelete("{id}")]
        [AuditLog("定时任务", "DELETE")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            await _scheduler.ReloadAsync();
            return Ok();
        }

        /// <summary>
        /// 手动强制触发一次任务执行（绕过 Cron 计划，仍受防重入保护）。
        /// 返回本次执行结果（含耗时/输出/错误）。
        /// </summary>
        [HttpPost("{id}/execute")]
        [AuditLog("定时任务", "EXECUTE")]
        public async Task<IActionResult> Execute(int id)
        {
            var executor = User.FindFirst(ClaimTypes.Name)?.Value
                           ?? User.FindFirst("username")?.Value
                           ?? User.Identity?.Name
                           ?? "unknown";
            var result = await _scheduler.RunAsync(id, executor);
            return Ok(result);
        }
    }
}
