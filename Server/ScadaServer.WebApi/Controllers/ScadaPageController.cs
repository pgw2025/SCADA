using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using System.Security.Claims;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScadaPageController : ControllerBase
    {
        private readonly IScadaPageAppService _appService;
        private readonly IConfigLogAppService _configLogAppService;

        public ScadaPageController(IScadaPageAppService appService, IConfigLogAppService configLogAppService)
        {
            _appService = appService;
            _configLogAppService = configLogAppService;
        }

        /// <summary>
        /// 阶段6-1：取当前操作用户名（来自 JWT 的 Name claim）。
        /// </summary>
        private string GetOperator()
            => User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "anonymous";

        /// <summary>
        /// 阶段6-1：写一条组态审计日志。复用现有 ConfigLog 表，
        /// DeviceId=0 作为「非设备对象（组态工程/页面）」哨兵，避免引入新表/迁移。
        /// </summary>
        private async Task AuditAsync(string changeDesc)
        {
            await _configLogAppService.CreateAsync(new ConfigLogDto
            {
                DeviceId = 0,
                Operator = GetOperator(),
                ChangeDesc = changeDesc,
                CreateTime = DateTime.Now
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? projectId)
            => Ok(await _appService.GetByProjectAsync(projectId));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var dto = await _appService.GetByIdAsync(id);
            if (dto == null) return NotFound();
            return Ok(dto);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ScadaPageDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var id = await _appService.CreateAsync(dto);
            dto.Id = id;
            await AuditAsync($"创建组态页面 [id={id}] 名称「{dto.Name}」(工程 {dto.ProjectId})");
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] ScadaPageDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var updated = await _appService.UpdateAsync(dto);
            if (!updated) return NotFound();
            await AuditAsync($"修改组态页面 [id={dto.Id}] 名称「{dto.Name}」");
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            await AuditAsync($"删除组态页面 [id={id}]");
            return NoContent();
        }

        /// <summary>
        /// 全量保存页面布局：请求体为该页全部组件（删旧全量 + 批量插入，事务内完成）
        /// </summary>
        [HttpPut("{id}/layout")]
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
            await AuditAsync($"保存页面布局 [pageId={id}] 共 {list.Count} 个组件");
            return NoContent();
        }
    }
}
