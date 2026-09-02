using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 创建用户 DTO（用于创建时提交用户名、密码及角色）。
    /// </summary>
    public class CreateUserDto
    {
        /// <summary>用户名；必填，唯一</summary>
        [Required(ErrorMessage = "用户名不能为空")]
        [StringLength(50, ErrorMessage = "用户名不能超过50个字符")]
        public string Username { get; set; } = string.Empty;

        /// <summary>初始密码；必填</summary>
        [Required(ErrorMessage = "密码不能为空")]
        [StringLength(128, ErrorMessage = "密码不能超过128个字符")]
        public string Password { get; set; } = string.Empty;

        /// <summary>用户角色，默认 Operator（操作员）</summary>
        [StringLength(20, ErrorMessage = "角色不能超过20个字符")]
        public string Role { get; set; } = "Operator";

        /// <summary>用户状态，默认 Active（启用）</summary>
        [StringLength(20, ErrorMessage = "状态不能超过20个字符")]
        public string Status { get; set; } = "Active";
    }
}
