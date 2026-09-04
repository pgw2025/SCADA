using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// HMI 组件模板控制器：组件库模板元数据的增删改查与导入导出（组件库动态化）。
    /// 读取：所有登录用户（运行态渲染依赖，FallbackPolicy 兜底）；
    /// 写操作 / 导入导出：RequireAdmin。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class HmiWidgetTemplateController : ApiControllerBase
    {
        private readonly IHmiWidgetTemplateAppService _appService;

        /// <summary>构造函数：注入模板应用服务。</summary>
        public HmiWidgetTemplateController(IHmiWidgetTemplateAppService appService)
            => _appService = appService;

        /// <summary>获取全部模板（按 SortOrder 升序、同序按 Id）。</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
            => Ok(await _appService.GetListAsync());

        /// <summary>按 ID 获取模板。</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var dto = await _appService.GetByIdAsync(id);
            return dto == null ? NotFound() : Ok(dto);
        }

        /// <summary>创建模板。</summary>
        [HttpPost]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Create([FromBody] HmiWidgetTemplateDto dto)
        {
            var guard = EnsureBody(dto); if (guard != null) return guard;
            var id = await _appService.CreateAsync(dto);
            dto.Id = id;
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        /// <summary>更新模板。</summary>
        [HttpPut]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Update([FromBody] HmiWidgetTemplateDto dto)
        {
            var guard = EnsureBody(dto); if (guard != null) return guard;
            return await _appService.UpdateAsync(dto) ? NoContent() : NotFound();
        }

        /// <summary>删除模板（系统内置模板拒绝删除）。</summary>
        [HttpDelete("{id}")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return NoContent();
        }

        /// <summary>导入模板（单条）：键冲突时按 ConflictMode 覆盖或改名另存。</summary>
        [HttpPost("import")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Import([FromBody] WidgetTemplateImportDto import)
        {
            var guard = EnsureBody(import); if (guard != null) return guard;
            return Ok(await _appService.ImportAsync(import));
        }

        /// <summary>批量导入：兼容单对象与 templates 数组两种载荷（D11），冲突统一走 rename。</summary>
        [HttpPost("import-bundle")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> ImportBundle([FromBody] WidgetTemplateBundleDto bundle)
        {
            var guard = EnsureBody(bundle); if (guard != null) return guard;
            var results = new List<ImportResult>();
            foreach (var t in bundle.Templates)
            {
                results.Add(await _appService.ImportAsync(
                    new WidgetTemplateImportDto { Template = t, ConflictMode = "rename" }));
            }
            return Ok(results);
        }

        /// <summary>导出模板（单条，JSON 附件下载）。</summary>
        [HttpGet("{id}/export")]
        public async Task<IActionResult> Export(int id)
        {
            var dto = await _appService.ExportAsync(id);
            var json = System.Text.Json.JsonSerializer.Serialize(dto,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json",
                $"{dto.Template.TemplateKey}.widget.json");
        }

        /// <summary>批量导出模板（多模板打一个文件，JSON 附件下载）。</summary>
        [HttpPost("export-bundle")]
        public async Task<IActionResult> ExportBundle([FromBody] int[] ids)
        {
            var guard = EnsureBody(ids); if (guard != null) return guard;
            var bundle = await _appService.ExportBundleAsync(ids);
            var json = System.Text.Json.JsonSerializer.Serialize(bundle,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json",
                $"widget-templates-{DateTime.UtcNow:yyyyMMddHHmmss}.widget.json");
        }
    }
}
