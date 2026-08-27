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
        [AuditLog("用户管理", "CREATE")]
        public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
        {
            // 透传 dto（含 Password），由应用服务完成哈希与校验
            await _appService.CreateAsync(dto);
            return Ok(new { Success = true, Message = "User created successfully" });
        }

        [HttpPut("{id}")]
        [AuditLog("用户管理", "UPDATE")]
        public async Task<IActionResult> Update(int id, [FromBody] SystemUserDto dto)
        {
            // 从路由取 id，避免前端漏传时静默更新失败（实体不存在会抛业务异常）
            dto.Id = id;
            await _appService.UpdateAsync(dto);
            return Ok();
        }

        [HttpDelete("{id}")]
        [AuditLog("用户管理", "DELETE")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok();
        }

        // 管理员重置他人密码（RequireAdmin 已由控制器类级约束）
        [HttpPost("{id}/reset-password")]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordDto dto)
        {
            await _appService.ResetPasswordAsync(id, dto.NewPassword);
            return Ok(new { Success = true, Message = "密码已重置" });
        }
    }
}
