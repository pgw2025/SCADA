using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 历史数据查询控制器（路由固定为 api/scada，与前端 historyApi 约定一致）
    /// </summary>
    [ApiController]
    [Route("api/scada")]
    public class HistoryController : ControllerBase
    {
        private readonly IHistoryAppService _appService;
        private readonly IHistoryMigrationService _migrationService;

        public HistoryController(IHistoryAppService appService, IHistoryMigrationService migrationService)
        {
            _appService = appService;
            _migrationService = migrationService;
        }

        /// <summary>
        /// 查询指定设备下某变量的历史记录（默认最近 100 条；支持时间范围与聚合降采样）。
        /// </summary>
        /// <param name="deviceKey">设备标识（可选，区分不同设备的同名变量）</param>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="limit">返回条数上限（默认 100）</param>
        /// <param name="start">起始时间（ISO 8601，可选）</param>
        /// <param name="end">结束时间（ISO 8601，可选）</param>
        /// <param name="aggregateWindowMs">聚合窗口（毫秒，可选）。>0 时按窗口均值聚合降采样，适合大时间范围趋势。</param>
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory(
            [FromQuery] string? deviceKey,
            [FromQuery] string variableKey,
            [FromQuery] int? limit = 100,
            [FromQuery] DateTime? start = null,
            [FromQuery] DateTime? end = null,
            [FromQuery] long? aggregateWindowMs = null)
        {
            var records = await _appService.GetHistoryAsync(
                deviceKey ?? string.Empty, variableKey, limit ?? 100, start, end, aggregateWindowMs);
            return Ok(records);
        }

        /// <summary>
        /// 触发一次性历史数据迁移（MySQL 存量 → 当前生效的 InfluxDB 历史库）。管理员权限。
        /// </summary>
        [HttpPost("history/migrate")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> MigrateHistory()
        {
            var result = await _migrationService.MigrateAsync();
            return Ok(result);
        }
    }
}
