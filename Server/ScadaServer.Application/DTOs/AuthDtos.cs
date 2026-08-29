namespace ScadaServer.Application.DTOs
{
    public class LoginDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>管理员重置他人密码请求。</summary>
    public class ResetPasswordDto
    {
        public string NewPassword { get; set; } = string.Empty;
    }

    /// <summary>用户自主修改密码请求（需验证原密码）。</summary>
    public class ChangePasswordDto
    {
        public string OldPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public SystemUserDto User { get; set; } = null!;
    }
}
