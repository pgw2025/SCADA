using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
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

        /// <summary>
        /// 导出工程为可迁移 JSON 文件（工程 + 全部画面 + 组件；变量绑定携带设备业务键）。
        /// 文件名 RFC 5987 编码支持中文；前端亦可自行用工程名命名下载。
        /// </summary>
        [HttpGet("{id}/export")]
        public async Task<IActionResult> Export(int id)
        {
            var package = await _appService.ExportAsync(id);
            if (package == null) return NotFound();

            var fileName = $"{SanitizeFileName(package.Project!.Name)}.scada-project.json";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(package, ScadaTransferJson.Options);
            Response.Headers.ContentDisposition =
                $"attachment; filename=\"scada-project.json\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
            await AuditAsync("EXPORT", id.ToString(), $"导出组态工程 [id={id}] 名称「{package.Project.Name}」");
            return File(bytes, "application/json;charset=utf-8");
        }

        /// <summary>
        /// 导入工程迁移包（事务整体创建新工程；同名自动加后缀；绑定按 DeviceKey 智能匹配）。
        /// 返回新工程 id/名称与 warnings（绑定失效等），由前端逐条提示。
        /// </summary>
        [HttpPost("import")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Import([FromBody] ScadaTransferPackageDto package)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var result = await _appService.ImportAsync(package);
                await AuditAsync("IMPORT", result.ProjectId.ToString(),
                    $"导入组态工程 [id={result.ProjectId}] 名称「{result.ProjectName}」" +
                    $"（画面 {result.ImportedPages}、组件 {result.ImportedComponents}、告警 {result.Warnings.Count}）");
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>剔除文件名非法字符，空名回退 export。</summary>
        private static string SanitizeFileName(string name)
        {
            var clean = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "export" : clean;
        }
    }
}
