using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using System.Security.Claims;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScadaProjectController : ControllerBase
    {
        private readonly IScadaProjectAppService _appService;
        private readonly IConfigLogAppService _configLogAppService;

        public ScadaProjectController(IScadaProjectAppService appService, IConfigLogAppService configLogAppService)
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
            await AuditAsync($"创建组态工程 [id={id}] 名称「{dto.Name}」");
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        [HttpPut]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Update([FromBody] ScadaProjectDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var updated = await _appService.UpdateAsync(dto);
            if (!updated) return NotFound();
            await AuditAsync($"修改组态工程 [id={dto.Id}] 名称「{dto.Name}」");
            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            await AuditAsync($"删除组态工程 [id={id}]");
            return NoContent();
        }
    }
}
