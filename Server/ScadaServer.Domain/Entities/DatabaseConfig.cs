using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据库配置实体
    /// </summary>
    [Table("DatabaseConfigs")]
    public class DatabaseConfig
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 配置名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 数据库类型
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// 后端类型
        /// </summary>
        public string BackendType { get; set; } = string.Empty;

        /// <summary>
        /// 主机地址
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 数据库名称（InfluxDB 场景下也可作为 Bucket 别名）
        /// </summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// 访问令牌（InfluxDB 2.x Token / 其它基于令牌的数据库）
        /// </summary>
        public string? Token { get; set; }

        /// <summary>
        /// 组织名（InfluxDB 2.x Organization）
        /// </summary>
        public string? Org { get; set; }

        /// <summary>
        /// Bucket 名称（InfluxDB 2.x 存储桶，等于历史数据的存储单元）
        /// </summary>
        public string? Bucket { get; set; }

        /// <summary>
        /// 是否当前生效（同一 Type 下仅一条生效，其余为备用清单）
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 最近一次连接测试结果（Ok / Failed）
        /// </summary>
        public string? LastStatus { get; set; }

        /// <summary>
        /// 最近一次连接测试时间
        /// </summary>
        public DateTime? LastCheckedAt { get; set; }
    }
}