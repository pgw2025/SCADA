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
    public class ScadaProjectController : ControllerBase
    {
        private readonly IScadaProjectAppService _appService;
        private readonly IOperationAuditService _auditService;
        private readonly ILogger<ScadaProjectController> _logger;

        public ScadaProjectController(IScadaProjectAppService appService, IOperationAuditService auditService,
            ILogger<ScadaProjectController> logger)
        {
            _appService = appService;
            _auditService = auditService;
            _logger = logger;
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

        /// <summary>
        /// 获取工程已授权用户列表（工程维度授权管理）。
        /// 授权语义：未授权的用户看不到该工程；Admin 恒可见全部工程，无需授权记录。
        /// </summary>
        [HttpGet("{id}/authorizations")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> GetAuthorizations(int id)
        {
            var users = await _appService.GetAuthorizedUsersAsync(id);
            if (users == null) return NotFound();
            return Ok(users);
        }

        /// <summary>
        /// 全量覆盖工程的授权用户集合（body: { userIds: [] }，空数组=清空授权）。
        /// 仅 Admin 可管理；授权变更写入操作审计（含新增/取消明细）。
        /// </summary>
        [HttpPut("{id}/authorizations")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> SaveAuthorizations(int id, [FromBody] SaveScadaProjectAuthorizationDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // 保存前取旧授权集合做 diff，生成「+新增 / -取消」审计明细
            var before = await _appService.GetAuthorizedUsersAsync(id);
            if (before == null) return NotFound();

            var oldIds = before.Select(u => u.UserId).ToHashSet();
            var newIds = (dto.UserIds ?? new List<int>()).ToHashSet();
            var added = newIds.Except(oldIds).ToList();
            var removed = oldIds.Except(newIds).ToList();

            try
            {
                var updated = await _appService.SaveAuthorizationsAsync(id, dto.UserIds);
                if (!updated) return NotFound();

                var detail = added.Count == 0 && removed.Count == 0
                    ? "授权无变化"
                    : $"授权变更：+新增用户[{string.Join(",", added)}]；-取消用户[{string.Join(",", removed)}]";
                await AuditAsync("AUTHORIZE", id.ToString(), $"修改组态工程授权 [id={id}]（{detail}）");
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "保存组态工程授权参数错误，ProjectId={ProjectId}", id);
                return BadRequest(new { message = ex.Message });
            }
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
        /// 导出=可迁移副本，仅 Admin 专属（与导入端点对称；被授权用户仅可查看/打开，不可导出）。
        /// </summary>
        [HttpGet("{id}/export")]
        [Authorize(Policy = "RequireAdmin")]
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
                _logger.LogWarning(ex, "导入组态工程参数错误。");
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
