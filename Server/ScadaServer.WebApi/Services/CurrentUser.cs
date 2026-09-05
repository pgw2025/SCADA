using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Constants;

namespace ScadaServer.WebApi.Services
{
    /// <summary>
    /// 当前请求用户实现：通过 IHttpContextAccessor 从 JWT claims 解析 UserId / IsAdmin / Username。
    /// 无 HttpContext（后台任务/测试）或 claims 缺失时返回安全默认值（UserId=0、IsAdmin=false），
    /// 最坏结果是授权判定收紧（空列表），而非越权。
    /// </summary>
    public class CurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUser(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <inheritdoc/>
        public int UserId
        {
            get
            {
                var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("id")?.Value;
                return int.TryParse(claim, out var id) ? id : 0;
            }
        }

        /// <inheritdoc/>
        public string? Username
            => _httpContextAccessor.HttpContext?.User?.FindFirst("username")?.Value
                ?? _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;

        /// <inheritdoc/>
        public bool IsAdmin
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null) return false;
                return user.IsInRole(SystemRoles.Admin)
                    || string.Equals(user.FindFirst("role")?.Value, SystemRoles.Admin, StringComparison.Ordinal);
            }
        }
    }
}
