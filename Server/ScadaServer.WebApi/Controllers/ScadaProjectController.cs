using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.WebApi.Services;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScadaProjectController : ControllerBase
    {
        private readonly IScadaProjectAppService _appService;
        private readonly IOperationAuditService _auditService;

        public ScadaProjectController(IScadaProjectAppService appService, IOperationAuditService auditService)
        {
            _appService = appService;
            _auditService = auditService;
        }

        /// <summary>
        /// 写一条组态工程审计日志（统一表 SystemLogs，Category=Operation）。
        /// </summary>
        private Task AuditAsync(string operation, string? relatedId, string description)
            => _auditService.RecordAsync("组态工程", operation, relatedId, description);

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var dto = await _appService.GetByIdAsync(id);
            if (dto == null) return NotFound();
            return Ok(dto);
        }

        /// <summary>
        /// 获取工程整树（工程 + 页面 + 组件），一次往返返回全部层级数据
        /// </summary>
        [HttpGet("{id}/full")]
        public async Task<IActionResult> GetFullTree(int id)
        {
            var tree = await _appService.GetTreeAsync(id);
            if (tree == null) return NotFound();
            return Ok(tree);
        }

        [HttpPost]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Create([FromBody] ScadaProjectDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var id = await _appService.CreateAsync(dto);
            dto.Id = id;
            await AuditAsync("CREATE", id.ToString(), $"创建组态工程 [id={id}] 名称「{dto.Name}」");
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        [HttpPut]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Update([FromBody] ScadaProjectDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var updated = await _appService.UpdateAsync(dto);
            if (!updated) return NotFound();
            await AuditAsync("UPDATE", dto.Id.ToString(), $"修改组态工程 [id={dto.Id}] 名称「{dto.Name}」");
            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            await AuditAsync("DELETE", id.ToString(), $"删除组态工程 [id={id}]");
            return NoContent();
        }
    }
}
