namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 登录请求 DTO（提交用户名与密码换取令牌）。
    /// </summary>
    public class LoginDto
    {
        /// <summary>登录用户名；必填</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>登录密码；必填</summary>
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>管理员重置他人密码请求。</summary>
    public class ResetPasswordDto
    {
        /// <summary>重置后的新密码；必填</summary>
        public string NewPassword { get; set; } = string.Empty;
    }

    /// <summary>用户自主修改密码请求（需验证原密码）。</summary>
    public class ChangePasswordDto
    {
        /// <summary>原密码，用于校验身份</summary>
        public string OldPassword { get; set; } = string.Empty;

        /// <summary>修改后的新密码</summary>
        public string NewPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// 登录响应 DTO（返回令牌与当前用户信息）。
    /// </summary>
    public class LoginResponseDto
    {
        /// <summary>登录是否成功</summary>
        public bool Success { get; set; }

        /// <summary>提示信息（成功或失败原因）</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>JWT 访问令牌（成功时返回）</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>当前登录用户信息（成功时返回）</summary>
        public SystemUserDto User { get; set; } = null!;
    }
}
