namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 创建用户 DTO（用于创建时提交用户名、密码及角色）。
    /// </summary>
    public class CreateUserDto
    {
        /// <summary>用户名；必填，唯一</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>初始密码；必填</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>用户角色，默认 Operator（操作员）</summary>
        public string Role { get; set; } = "Operator";

        /// <summary>用户状态，默认 Active（启用）</summary>
        public string Status { get; set; } = "Active";
    }
}
