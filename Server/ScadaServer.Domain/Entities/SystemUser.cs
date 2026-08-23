using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 系统用户实体
    /// </summary>
    [SugarTable("SystemUsers")]
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
    }
}