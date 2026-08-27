using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 系统用户实体
    /// </summary>
    [Table("SystemUsers")]
    public class SystemUser : EntityBase
    {
        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// 密码哈希值
        /// </summary>
        public string PasswordHash { get; set; }

        /// <summary>
        /// 用户角色
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// 用户状态（如：Active、Inactive）
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 创建时间（UTC）。新建时由服务层写入，供前端口令审计展示。
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}