using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 报警记录控制器：查询与确认（认证用户读，确认是操作员职责不设 Admin 门槛）。
    /// 报警记录由运行时写入，对外不开放创建/更新/删除，防伪造报警数据。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AlarmRecordController : ControllerBase
    {
        private readonly IAlarmRecordAppService _appService;

        public AlarmRecordController(IAlarmRecordAppService appService)
        {
            _appService = appService;
        }

        /// <summary>
        /// 分页查询报警记录。
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] AlarmRecordQueryDto query)
            => Ok(await _appService.QueryAsync(query));

        /// <summary>
        /// 查询当前未恢复报警记录（实时列表初始化）。
        /// </summary>
        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
            => Ok(await _appService.GetActiveAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(long id)
        {
            var dto = await _appService.GetByIdAsync(id);
            return dto == null ? NotFound() : Ok(dto);
        }

        /// <summary>
        /// 确认报警记录（设置确认人/时间）。
        /// </summary>
        [HttpPut("{id}/ack")]
        public async Task<IActionResult> Ack(long id)
        {
            var ackBy = User.FindFirst(ClaimTypes.Name)?.Value
                        ?? User.FindFirst("username")?.Value
                        ?? User.Identity?.Name;
            var ok = await _appService.AckAsync(id, ackBy ?? "unknown");
            return ok ? Ok() : NotFound();
        }
    }
}