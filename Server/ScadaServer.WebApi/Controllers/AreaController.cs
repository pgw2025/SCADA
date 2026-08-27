using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class AreaController : ControllerBase
    {
        private readonly IAreaAppService _appService;

        public AreaController(IAreaAppService appService)
        {
            _appService = appService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _appService.GetByIdAsync(id));

        [HttpPost]
        [AuditLog("区域管理", "CREATE")]
        public async Task<IActionResult> Create([FromBody] AreaDto dto)
        {
            var result = await _appService.CreateAsync(dto);
            return Ok(result);
        }

        [HttpPut]
        [AuditLog("区域管理", "UPDATE")]
        public async Task<IActionResult> Update([FromBody] AreaDto dto)
        {
            var result = await _appService.UpdateAsync(dto);
            return Ok(result);
        }

        [HttpDelete("{id}")]
        [AuditLog("区域管理", "DELETE")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok(new { success = true, message = "区域删除成功" });
        }
    }
}
