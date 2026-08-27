using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.WebApi.Services;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScadaPageController : ControllerBase
    {
        private readonly IScadaPageAppService _appService;
        private readonly IOperationAuditService _auditService;

        public ScadaPageController(IScadaPageAppService appService, IOperationAuditService auditService)
        {
            _appService = appService;
            _auditService = auditService;
        }

        /// <summary>
        /// 写一条组态页面审计日志（统一表 SystemLogs，Category=Operation）。
        /// </summary>
        private Task AuditAsync(string operation, string? relatedId, string description)
            => _auditService.RecordAsync("组态页面", operation, relatedId, description);

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? projectId, [FromQuery] string? platform)
            => Ok(await _appService.GetByProjectAsync(projectId, platform));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var dto = await _appService.GetByIdAsync(id);
            if (dto == null) return NotFound();
            return Ok(dto);
        }

        [HttpPost]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Create([FromBody] ScadaPageDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var id = await _appService.CreateAsync(dto);
            dto.Id = id;
            await AuditAsync("CREATE", id.ToString(), $"创建组态页面 [id={id}] 名称「{dto.Name}」(工程 {dto.ProjectId})");
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        [HttpPut]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Update([FromBody] ScadaPageDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var updated = await _appService.UpdateAsync(dto);
            if (!updated) return NotFound();
            await AuditAsync("UPDATE", dto.Id.ToString(), $"修改组态页面 [id={dto.Id}] 名称「{dto.Name}」");
            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            await AuditAsync("DELETE", id.ToString(), $"删除组态页面 [id={id}]");
            return NoContent();
        }

        /// <summary>
        /// 全量保存页面布局：请求体为该页全部组件（删旧全量 + 批量插入，事务内完成）
        /// </summary>
        [HttpPut("{id}/layout")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> SaveLayout(int id, [FromBody] List<HmiComponentDto> components)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var list = components ?? new List<HmiComponentDto>();
            try
            {
                await _appService.SaveLayoutAsync(id, list);
            }
            catch (ScadaServer.Domain.Exceptions.BusinessException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
            await AuditAsync("EXECUTE", id.ToString(), $"保存页面布局 [pageId={id}] 共 {list.Count} 个组件");
            return NoContent();
        }
    }
}
