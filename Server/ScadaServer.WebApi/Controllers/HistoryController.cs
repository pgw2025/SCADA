using Microsoft.AspNetCore.Mvc;
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

        public HistoryController(IHistoryAppService appService)
        {
            _appService = appService;
        }

        /// <summary>
        /// 查询指定变量的最近历史记录（默认最近 100 条）。
        /// </summary>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="limit">返回条数上限</param>
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory(
            [FromQuery] string variableKey,
            [FromQuery] int? limit = 100)
        {
            var records = await _appService.GetHistoryAsync(variableKey, limit ?? 100);
            return Ok(records);
        }
    }
}
