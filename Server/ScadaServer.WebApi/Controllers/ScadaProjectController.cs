using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScadaProjectController : ControllerBase
    {
        private readonly IScadaProjectAppService _appService;

        public ScadaProjectController(IScadaProjectAppService appService)
        {
            _appService = appService;
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
        public async Task<IActionResult> Create([FromBody] ScadaProjectDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var id = await _appService.CreateAsync(dto);
            dto.Id = id;
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] ScadaProjectDto dto)
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
    }
}
