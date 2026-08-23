using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlarmRuleController : ControllerBase
    {
        private readonly IAlarmRuleAppService _appService;

        public AlarmRuleController(IAlarmRuleAppService appService)
        {
            _appService = appService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AlarmRuleDto dto)
        {
            await _appService.CreateAsync(dto);
            return Ok(dto);
        }

        [HttpPut("{id}/toggle")]
        public async Task<IActionResult> Toggle(int id, [FromBody] bool enabled)
        {
            var dto = await _appService.GetByIdAsync(id);
            if (dto != null)
            {
                dto.IsEnabled = enabled;
                await _appService.UpdateAsync(dto);
            }
            return Ok();
        }
    }
}
