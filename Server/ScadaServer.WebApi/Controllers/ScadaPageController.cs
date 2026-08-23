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
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _appService.GetByIdAsync(id));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ScadaPageDto dto)
        {
            await _appService.CreateAsync(dto);
            return Ok(dto);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] ScadaPageDto dto)
        {
            await _appService.UpdateAsync(dto);
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok();
        }
    }
}
