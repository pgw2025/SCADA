using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScadaPageController : ControllerBase
    {
        private readonly IScadaPageAppService _appService;

        public ScadaPageController(IScadaPageAppService appService)
        {
            _appService = appService;
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
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] ScadaPageDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var updated = await _appService.UpdateAsync(dto);
            if (!updated) return NotFound();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return NoContent();
        }

        /// <summary>
        /// 全量保存页面布局：请求体为该页全部组件（删旧全量 + 批量插入，事务内完成）
        /// </summary>
        [HttpPut("{id}/layout")]
        public async Task<IActionResult> SaveLayout(int id, [FromBody] List<HmiComponentDto> components)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                await _appService.SaveLayoutAsync(id, components ?? new List<HmiComponentDto>());
            }
            catch (ScadaServer.Domain.Exceptions.BusinessException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
            return NoContent();
        }
    }
}
