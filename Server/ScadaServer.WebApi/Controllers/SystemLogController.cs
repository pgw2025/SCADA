using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 系统日志查询控制器。
    /// 日志仅由系统内部写入（运行采集 / 操作审计），对外不开放创建/更新/单条删除接口，防伪造日志。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SystemLogController : ControllerBase
    {
        private readonly ISystemLogAppService _appService;

        public SystemLogController(ISystemLogAppService appService)
        {
            _appService = appService;
        }

        /// <summary>
        /// 分页查询系统日志（分类/级别/关键字/时间段）。
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>分页结果 { total, items }</returns>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] SystemLogQueryDto query)
            => Ok(await _appService.QueryAsync(query));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _appService.GetByIdAsync(id));

        /// <summary>
        /// 按分类/时间段批量清理日志（仅 Admin）。
        /// 必须显式指定时间范围，防止误删全部日志；前端需二次确认。
        /// </summary>
        [Authorize(Policy = "RequireAdmin")]
        [HttpPost("clear")]
        public async Task<IActionResult> Clear([FromBody] SystemLogClearDto dto)
        {
            var deleted = await _appService.ClearAsync(dto?.Category, dto?.StartTime, dto?.EndTime);
            return Ok(new { Success = true, Message = $"已清理 {deleted} 条日志", Deleted = deleted });
        }
    }
}
