using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 通信协议控制器：管理系统所支持的通信协议（协议/驱动解耦）。
    /// 协议是"数据模型如何通信"的真相源，前端创建数据模型时的协议下拉选择由本控制器提供数据源。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class ProtocolController : ControllerBase
    {
        private readonly IProtocolAppService _appService;

        public ProtocolController(IProtocolAppService appService)
        {
            _appService = appService;
        }

        /// <summary>获取全部协议（创建数据模型时下拉选择的数据源）。</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        /// <summary>按 ID 获取协议。</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _appService.GetByIdAsync(id);
            return result == null ? NotFound() : Ok(result);
        }

        /// <summary>按协议键获取协议（如 /api/Protocol/{key:s7}）。</summary>
        [HttpGet("{key:alpha}")]
        public async Task<IActionResult> GetByKey(string key)
        {
            var result = await _appService.GetByKeyAsync(key);
            return result == null ? NotFound() : Ok(result);
        }

        /// <summary>创建协议。</summary>
        [HttpPost]
        [AuditLog("通信协议", "CREATE")]
        public async Task<IActionResult> Create([FromBody] CreateProtocolDto dto)
            => Ok(await _appService.CreateAsync(dto));

        /// <summary>更新协议。</summary>
        [HttpPut("{id}")]
        [AuditLog("通信协议", "UPDATE")]
        public async Task<IActionResult> Update(int id, [FromBody] ProtocolDto dto)
            => Ok(await _appService.UpdateAsync(id, dto));

        /// <summary>删除协议（已被数据模型绑定的协议不可删除）。</summary>
        [HttpDelete("{id}")]
        [AuditLog("通信协议", "DELETE")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok(new { success = true, message = "协议删除成功" });
        }
    }
}