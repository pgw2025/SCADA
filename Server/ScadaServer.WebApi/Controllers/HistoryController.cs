using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.DTOs;
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
        /// 查询指定设备下某变量的历史记录（默认最近 100 条；支持时间范围、聚合降采样与聚合函数）。
        /// </summary>
        /// <param name="deviceKey">设备标识（可选，区分不同设备的同名变量）</param>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="limit">返回条数上限（默认 100）</param>
        /// <param name="start">起始时间（ISO 8601，可选）</param>
        /// <param name="end">结束时间（ISO 8601，可选）</param>
        /// <param name="aggregateWindowMs">聚合窗口（毫秒，可选）。>0 时按窗口均值聚合降采样，适合大时间范围趋势。</param>
        /// <param name="aggregateFn">聚合函数（mean/max/min/first/last，默认 mean；仅聚合窗口 >0 时生效）</param>
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory(
            [FromQuery] string? deviceKey,
            [FromQuery] string variableKey,
            [FromQuery] int? limit = 100,
            [FromQuery] DateTime? start = null,
            [FromQuery] DateTime? end = null,
            [FromQuery] long? aggregateWindowMs = null,
            [FromQuery] string? aggregateFn = null)
        {
            var records = await _appService.GetHistoryAsync(
                deviceKey ?? string.Empty, variableKey, limit ?? 100, start, end, aggregateWindowMs, aggregateFn ?? "mean");
            return Ok(records);
        }

        /// <summary>
        /// 批量查询多个变量的历史序列（各变量独立返回，互不混入；变量数上限 8）。
        /// </summary>
        [HttpPost("history/batch")]
        public async Task<IActionResult> GetHistoryBatch([FromBody] HistoryBatchRequestDto request)
        {
            var result = await _appService.GetHistoryBatchAsync(request);
            return Ok(result);
        }

        /// <summary>
        /// 导出历史数据为 CSV（UTF-8 带 BOM，Excel 直接可开；长表格式按时间升序）。
        /// 管理员权限。行数上限 50000，超出时建议缩小时间范围或增大聚合窗口。
        /// </summary>
        /// <param name="vars">待导出变量，重复参数格式 deviceKey:variableKey（如 vars=a:b&amp;vars=c:d）</param>
        /// <param name="start">起始时间（ISO 8601，可选）</param>
        /// <param name="end">结束时间（ISO 8601，可选）</param>
        /// <param name="limit">导出行数上限（默认 50000，上限 50000）</param>
        /// <param name="aggregateWindowMs">聚合窗口（毫秒，可选）</param>
        /// <param name="aggregateFn">聚合函数（默认 mean）</param>
        [HttpGet("history/export")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> ExportHistory(
            [FromQuery] string[]? vars,
            [FromQuery] DateTime? start = null,
            [FromQuery] DateTime? end = null,
            [FromQuery] int? limit = 50000,
            [FromQuery] long? aggregateWindowMs = null,
            [FromQuery] string? aggregateFn = null)
        {
            var variables = new List<HistoryBatchVariableDto>();
            foreach (var v in vars ?? Array.Empty<string>())
            {
                var parts = v.Split(':');
                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[^1])) continue;
                variables.Add(new HistoryBatchVariableDto
                {
                    DeviceKey = parts.Length > 1 ? parts[0] : string.Empty,
                    VariableKey = parts[^1]
                });
            }

            if (variables.Count == 0)
            {
                return BadRequest(new { message = "未指定任何待导出变量（vars=deviceKey:variableKey）" });
            }

            var exportLimit = limit ?? 50000;
            if (exportLimit <= 0) exportLimit = 50000;
            if (exportLimit > 50000) exportLimit = 50000;

            var bytes = await _appService.ExportCsvAsync(
                variables, start, end, aggregateWindowMs, aggregateFn ?? "mean", exportLimit);

            var filename = $"history_export_{DateTime.UtcNow:yyyyMMddHHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", filename);
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
