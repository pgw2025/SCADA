using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.WebApi.Services;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 组态画面文件夹控制器：管理画面列表文件夹的分类、移动、删除与排序（仅 Desktop/Mobile 端）。
    /// 读取类操作供编辑器专用数据面使用，与页面/组件一致收紧为 Admin 专属。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ScadaPageFolderController : ControllerBase
    {
        private readonly IScadaPageFolderAppService _folderService;
        private readonly IOperationAuditService _auditService;
        private readonly ILogger<ScadaPageFolderController> _logger;

        public ScadaPageFolderController(
            IScadaPageFolderAppService folderService,
            IOperationAuditService auditService,
            ILogger<ScadaPageFolderController> logger)
        {
            _folderService = folderService;
            _auditService = auditService;
            _logger = logger;
        }

        /// <summary>写一条组态画面文件夹审计日志（统一表 SystemLogs，Category=Operation）。</summary>
        private Task AuditAsync(string operation, string? relatedId, string description)
            => _auditService.RecordAsync("组态文件夹", operation, relatedId, description);

        /// <summary>查询指定工程的全部画面文件夹。</summary>
        [HttpGet]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> GetByProject([FromQuery] int projectId)
            => Ok(await _folderService.GetByProjectAsync(projectId));

        /// <summary>创建画面文件夹，返回生成的主键。</summary>
        [HttpPost]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Create([FromBody] ScadaPageFolderDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var id = await _folderService.CreateAsync(dto);
                dto.Id = id;
                await AuditAsync("CREATE", id.ToString(), $"创建画面文件夹 [id={id}] 名称「{dto.Name}」(工程 {dto.ProjectId})");
                return Ok(new { id });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "创建画面文件夹参数错误，ProjectId={ProjectId}", dto.ProjectId);
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>更新画面文件夹（重命名/移动父级）。</summary>
        [HttpPut]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Update([FromBody] ScadaPageFolderDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var updated = await _folderService.UpdateAsync(dto);
                if (!updated) return NotFound();
                await AuditAsync("UPDATE", dto.Id.ToString(), $"修改画面文件夹 [id={dto.Id}] 名称「{dto.Name}」");
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "更新画面文件夹参数错误，FolderId={FolderId}", dto.Id);
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// 删除画面文件夹：默认 mode=reparent（内容上提一级），可选 mode=cascade（连同子夹与画面删除）。
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Delete(int id, [FromQuery] string? mode)
        {
            try
            {
                await _folderService.DeleteAsync(id, mode);
                await AuditAsync("DELETE", id.ToString(), $"删除画面文件夹 [id={id}] (mode={(string.IsNullOrWhiteSpace(mode) ? "reparent" : mode)})");
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "删除画面文件夹参数错误，FolderId={FolderId}", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>同级统一排序：文件夹段 + 画面段各自全量覆盖。</summary>
        [HttpPost("reorder")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Reorder([FromBody] FolderReorderDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                await _folderService.ReorderAsync(dto);
                await AuditAsync("REORDER", dto.ParentFolderId?.ToString() ?? string.Empty,
                    $"重排画面列表顺序（父级={(dto.ParentFolderId?.ToString() ?? "根级")}）");
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "重排画面列表参数错误");
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}