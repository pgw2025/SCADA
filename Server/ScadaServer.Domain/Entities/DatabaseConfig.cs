using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据库配置实体
    /// </summary>
    [SugarTable("DatabaseConfigs")]
    public class DatabaseConfig : EntityBase
    {
        /// <summary>
        /// 配置名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 数据库类型
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 后端类型
        /// </summary>
        public string BackendType { get; set; }

        /// <summary>
        /// 主机地址
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 数据库名称
        /// </summary>
        public string DatabaseName { get; set; }
    }
}