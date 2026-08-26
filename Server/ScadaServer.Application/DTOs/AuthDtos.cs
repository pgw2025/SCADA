namespace ScadaServer.Application.DTOs
{
    public class LoginDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    /// <summary>管理员重置他人密码请求。</summary>
    public class ResetPasswordDto
    {
        public string NewPassword { get; set; }
    }

    /// <summary>用户自主修改密码请求（需验证原密码）。</summary>
    public class ChangePasswordDto
    {
        public string OldPassword { get; set; }
        public string NewPassword { get; set; }
    }

    public class LoginResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Token { get; set; }
        public SystemUserDto User { get; set; }
    }
}
