using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 认证控制器，处理用户登录
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ISystemUserAppService _userService;

        /// <summary>
        /// 初始化认证控制器
        /// </summary>
        /// <param name="userService">用户服务</param>
        public AuthController(ISystemUserAppService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// 用户登录
        /// </summary>
        /// <param name="loginDto">登录信息</param>
        /// <returns>登录结果</returns>
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            var result = await _userService.LoginAsync(loginDto);
            if (!result.Success) return Unauthorized(result);
            return Ok(result);
        }

        /// <summary>
        /// 回源当前登录用户：读取数据库中的最新角色/状态（而非 token 中的快照）。
        /// 供前端刷新后获取权威身份；账号不存在或已被停用返回 401，由前端清除本地会话。
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var idClaim = User.FindFirst("id")?.Value;
            if (!int.TryParse(idClaim, out var userId))
            {
                return Unauthorized(new { Message = "无法识别当前用户" });
            }

            var user = await _userService.GetByIdAsync(userId);
            if (user == null || user.Status != "Active")
            {
                return Unauthorized(new { Message = "账号不存在或已被停用" });
            }

            return Ok(new { user.Username, user.Role, user.Status });
        }

        // 用户自主修改密码（任意已登录用户，当前用户 id 取自 JWT；依赖全局认证兜底策略）
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var idClaim = User.FindFirst("id")?.Value;
            if (!int.TryParse(idClaim, out var userId))
            {
                return Unauthorized(new { Message = "无法识别当前用户" });
            }
            await _userService.ChangePasswordAsync(userId, dto.OldPassword, dto.NewPassword);
            return Ok(new { Success = true, Message = "密码修改成功" });
        }
    }
}
