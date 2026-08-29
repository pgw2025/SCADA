using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class ModelVariableController : ControllerBase
    {
        private readonly IModelVariableAppService _appService;

        public ModelVariableController(IModelVariableAppService appService)
        {
            _appService = appService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _appService.GetByIdAsync(id);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ModelVariableDto dto)
        {
            var result = await _appService.CreateAsync(dto);
            return Ok(result);
        }

        /// <summary>
        /// 按数据模型ID查询其变量列表（字面量段 by-model 优先于 {id} 路由，不冲突）
        /// </summary>
        [HttpGet("by-model/{modelId}")]
        public async Task<IActionResult> GetByModelId(int modelId)
        {
            var result = await _appService.GetByModelIdAsync(modelId);
            return Ok(result);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ModelVariableDto dto)
        {
            // 从路由取 id，与前端 variableApi PUT /api/ModelVariable/{id} 契约对齐
            dto.Id = id;
            var result = await _appService.UpdateAsync(dto);
            return Ok(result);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok(new { success = true, message = "数据变量删除成功" });
        }

        // ---- 导入 / 导出 ----

        /// <summary>
        /// 导入预览：解析上传文件（TIA xlsx / 标准 CSV），返回逐行结果与模型内冲突标记，不入库。
        /// </summary>
        [HttpPost("import/preview")]
        public async Task<IActionResult> ImportPreview([FromForm] int modelId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "请选择要导入的文件" });

            using var stream = file.OpenReadStream();
            var result = await _appService.ImportPreviewAsync(modelId, stream, file.FileName);
            return Ok(result);
        }

        /// <summary>
        /// 确认导入：按 conflictStrategy 批量写入（Skip/Overwrite/Abort）。
        /// </summary>
        [HttpPost("import")]
        public async Task<IActionResult> Import([FromForm] int modelId, IFormFile file, [FromForm] string conflictStrategy = "Skip")
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "请选择要导入的文件" });

            if (!Enum.TryParse<ConflictStrategy>(conflictStrategy, true, out var strategy))
                return BadRequest(new { message = $"非法的冲突策略 '{conflictStrategy}'（可选 Skip/Overwrite/Abort）" });

            using var stream = file.OpenReadStream();
            var result = await _appService.ImportAsync(modelId, stream, file.FileName, strategy);
            return Ok(result);
        }

        /// <summary>
        /// 导出模型变量。format=xlsx|csv，列与导入模板一致，文件可直接再导入。
        /// </summary>
        [HttpGet("by-model/{modelId}/export")]
        public async Task<IActionResult> Export(int modelId, [FromQuery] string format = "xlsx")
        {
            var bytes = await _appService.ExportAsync(modelId, format);
            var isCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
            var ext = isCsv ? "csv" : "xlsx";
            var content = isCsv
                ? "text/csv; charset=utf-8"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            var fileName = $"Model-{modelId}-Variables-{DateTime.UtcNow:yyyyMMddHHmmss}.{ext}";
            return File(bytes, content, fileName);
        }
    }
}
