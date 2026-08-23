using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemUserController : ControllerBase
    {
        private readonly ISystemUserAppService _appService;

        public SystemUserController(ISystemUserAppService appService)
        {
            _appService = appService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _appService.GetListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id) => Ok(await _appService.GetByIdAsync(id));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
        {
            await _appService.CreateAsync(new SystemUserDto
            {
                Username = dto.Username,
                Role = dto.Role,
                Status = dto.Status
            });
            return Ok(new { Success = true, Message = "User created successfully" });
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] SystemUserDto dto)
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
