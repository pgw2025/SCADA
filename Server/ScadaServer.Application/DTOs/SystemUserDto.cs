using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 系统用户 DTO（不含密码哈希，用于列表/详情展示）。
    /// </summary>
    public class SystemUserDto
    {
        /// <summary>用户ID（主键）</summary>
        public int Id { get; set; }

        /// <summary>用户名（登录标识，唯一）</summary>
        [Required(ErrorMessage = "用户名不能为空")]
        [StringLength(50, ErrorMessage = "用户名不能超过50个字符")]
        public string Username { get; set; } = string.Empty;

        /// <summary>用户角色（如 Admin / Operator）</summary>
        [StringLength(20, ErrorMessage = "角色不能超过20个字符")]
        public string Role { get; set; } = string.Empty;

        /// <summary>用户状态（Active / Disabled）</summary>
        [StringLength(20, ErrorMessage = "状态不能超过20个字符")]
        public string Status { get; set; } = string.Empty;

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; }
    }
}
