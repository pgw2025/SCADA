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
            // 透传 dto（含 Password），由应用服务完成哈希与校验
            await _appService.CreateAsync(dto);
            return Ok(new { Success = true, Message = "User created successfully" });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] SystemUserDto dto)
        {
            // 从路由取 id，避免前端漏传时静默更新失败（实体不存在会抛业务异常）
            dto.Id = id;
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
