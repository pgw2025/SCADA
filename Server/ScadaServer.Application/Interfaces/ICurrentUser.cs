namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 当前请求用户抽象：从 JWT claims 解析。
    /// 后台任务/测试环境下无 HttpContext，实现应返回默认值（UserId=0、IsAdmin=false、Username=null）。
    /// 定义于 Application 层：应用服务校验授权时不依赖 WebApi 层的 HttpContext 类型。
    /// </summary>
    public interface ICurrentUser
    {
        /// <summary>
        /// 当前用户 Id（JWT claim "id"），未登录/解析失败为 0。
        /// </summary>
        int UserId { get; }

        /// <summary>
        /// 当前用户名（JWT claim "username"，回退 ClaimTypes.Name），可能为 null。
        /// </summary>
        string? Username { get; }

        /// <summary>
        /// 是否 Admin 角色（JWT claim "role" == SystemRoles.Admin）。
        /// </summary>
        bool IsAdmin { get; }
    }
}
