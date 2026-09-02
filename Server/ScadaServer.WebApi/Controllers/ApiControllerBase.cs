using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// Web API 控制器基类：统一提供入参守卫，避免各控制器内联重复的 null/空校验。
    /// 校验失败统一返回 ApiResponse 结构与全局异常中间件保持一致。
    /// </summary>
    [ApiController]
    public abstract class ApiControllerBase : ControllerBase
    {
        /// <summary>
        /// 守卫 [FromBody] 请求体为 null（请求内容为字面量 null 或 JSON null）。
        /// 此场景 [ApiController] 默认不拦截，需显式校验，否则后续访问成员会 NRE。
        /// </summary>
        /// <param name="body">请求体对象</param>
        /// <param name="message">失败提示</param>
        /// <returns>校验通过返回 null，否则返回统一的 400 响应</returns>
        protected IActionResult? EnsureBody<T>(T? body, string message = "请求体缺失")
            where T : class
            => body is null ? BadRequest(ApiResponse.Fail(message)) : null;

        /// <summary>
        /// 守卫字符串参数非 null/非空（对纯 string / 裸值参数有效）。
        /// </summary>
        /// <param name="value">待校验字符串</param>
        /// <param name="name">参数名（用于提示）</param>
        /// <param name="message">可选自定义提示</param>
        /// <returns>校验通过返回 null，否则返回统一的 400 响应</returns>
        protected IActionResult? EnsureNotBlank(string? value, string name = "参数", string? message = null)
            => string.IsNullOrWhiteSpace(value)
                ? BadRequest(ApiResponse.Fail(message ?? $"{name}不能为空"))
                : null;
    }
}