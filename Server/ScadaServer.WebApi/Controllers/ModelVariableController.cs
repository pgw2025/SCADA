using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] ModelVariableDto dto)
        {
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
