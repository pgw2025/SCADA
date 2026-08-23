using Microsoft.AspNetCore.Mvc;
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
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            var result = await _userService.LoginAsync(loginDto);
            if (!result.Success) return Unauthorized(result);
            return Ok(result);
        }
    }
}
