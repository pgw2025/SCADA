using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
        private readonly IScadaProjectAppService _projectAppService;
        private readonly IOperationAuditService _auditService;
        private readonly ILogger<ScadaPageController> _logger;

        public ScadaPageController(
            IScadaPageAppService appService,
            IScadaProjectAppService projectAppService,
            IOperationAuditService auditService,
            ILogger<ScadaPageController> logger)
        {
            _appService = appService;
            _projectAppService = projectAppService;
            _auditService = auditService;
            _logger = logger;
        }

        /// <summary>
        /// 写一条组态页面审计日志（统一表 SystemLogs，Category=Operation）。
        /// </summary>
        private Task AuditAsync(string operation, string? relatedId, string description)
            => _auditService.RecordAsync("组态页面", operation, relatedId, description);

        // 组态页面读取/画面导出为编辑器专用数据面（播放器仅经 /ScadaProject/{id}/full 整树获取），
        // 与工程授权配套收紧为 Admin 专属，防止 Operator 旁路枚举页面数据或导出任意画面 JSON。
        [HttpGet]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> GetAll([FromQuery] int? projectId, [FromQuery] string? platform)
            => Ok(await _appService.GetByProjectAsync(projectId, platform));

        [HttpGet("{id}")]
        [Authorize(Policy = "RequireAdmin")]
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

        /// <summary>导出单个画面为可迁移 JSON 文件（含全部组件，绑定携带设备业务键）。</summary>
        [HttpGet("{id}/export")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Export(int id)
        {
            var package = await _projectAppService.ExportPageAsync(id);
            if (package == null) return NotFound();

            var fileName = $"{SanitizeFileName(package.Pages[0].Name)}.scada-page.json";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(package, ScadaTransferJson.Options);
            Response.Headers.ContentDisposition =
                $"attachment; filename=\"scada-page.json\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
            await AuditAsync("EXPORT", id.ToString(), $"导出组态画面 [id={id}] 名称「{package.Pages[0].Name}」");
            return File(bytes, "application/json;charset=utf-8");
        }

        /// <summary>导入画面迁移包到指定工程（重名自动加后缀；同端已有首页时降级为普通画面）。</summary>
        [HttpPost("import")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Import([FromQuery] int projectId, [FromBody] ScadaTransferPackageDto package)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var result = await _projectAppService.ImportPageAsync(projectId, package);
                await AuditAsync("IMPORT", result.PageId?.ToString() ?? string.Empty,
                    $"导入组态画面 [id={result.PageId}] 名称「{result.PageName}」到工程 {projectId}");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "导入组态画面参数错误，ProjectId={ProjectId}", projectId);
                return BadRequest(new { message = ex.Message });
            }
        }

        private static string SanitizeFileName(string name)
        {
            var clean = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "export" : clean;
        }
    }
}
