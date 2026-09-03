using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 控制器管理控制器：管理控制器/PLC 资产台账（阶段 2）。
    /// 路由使用 /api/controllers，避免与现有 DeviceController 等控制器路由冲突。
    /// 本阶段仅资产登记（CRUD/列表/下拉），不产生任何采集行为。
    /// </summary>
    [ApiController]
    [Route("api/controllers")]
    [Authorize(Policy = "RequireAdmin")]
    public class ControllerManagementController : ControllerBase
    {
        private readonly IControllerAppService _appService;

        public ControllerManagementController(IControllerAppService appService)
        {
            _appService = appService;
        }

        /// <summary>分页查询控制器（支持按协议/关键字过滤）。</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] ControllerQueryDto query)
            => Ok(await _appService.QueryAsync(query));

        /// <summary>控制器下拉数据源（Id+Code+Name+Protocol）。</summary>
        [HttpGet("options")]
        public async Task<IActionResult> GetOptions()
            => Ok(await _appService.GetOptionsAsync());

        /// <summary>按 ID 获取控制器。</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _appService.GetByIdAsync(id);
            return result == null ? NotFound() : Ok(result);
        }

        /// <summary>创建控制器。</summary>
        [HttpPost]
        [AuditLog("控制器管理", "CREATE")]
        public async Task<IActionResult> Create([FromBody] CreateControllerDto dto)
            => Ok(await _appService.CreateAsync(dto));

        /// <summary>更新控制器。</summary>
        [HttpPut("{id:int}")]
        [AuditLog("控制器管理", "UPDATE")]
        public async Task<IActionResult> Update(int id, [FromBody] CreateControllerDto dto)
            => Ok(await _appService.UpdateAsync(id, dto));

        /// <summary>删除控制器。</summary>
        [HttpDelete("{id:int}")]
        [AuditLog("控制器管理", "DELETE")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok(new { success = true, message = "控制器删除成功" });
        }
    }
}
