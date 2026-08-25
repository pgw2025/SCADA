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
    }
}
