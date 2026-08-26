namespace ScadaServer.Domain.Constants
{
    /// <summary>
    /// 系统角色常量——全系统角色值域的唯一事实来源。
    /// <para>
    /// 角色以字符串存库并被写入 JWT 的 role claim，因此这里使用 const string 而非枚举，
    /// 以便与 [Authorize(Roles=...)] 的精确字符串匹配保持一致。
    /// </para>
    /// </summary>
    public static class SystemRoles
    {
        /// <summary>管理员：可访问配置、用户管理等全部管理接口。</summary>
        public const string Admin = "Admin";

        /// <summary>操作员：可访问运行态读取接口与下发控制。</summary>
        public const string Operator = "Operator";

        /// <summary>观察员：只读权限。</summary>
        public const string Viewer = "Viewer";

        /// <summary>合法角色白名单，用于服务层输入校验。</summary>
        public static readonly string[] All = { Admin, Operator, Viewer };
    }
}