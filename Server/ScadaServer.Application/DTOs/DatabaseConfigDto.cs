using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 数据库配置 DTO。
    /// <para>
    /// 密码/令牌在回显时一律掩码（<see cref="HasPassword"/> / <see cref="HasToken"/> 标记是否已配置），
    /// 保存时若值为掩码或空则视为“保持原值不修改”。
    /// </para>
    /// </summary>
    public class DatabaseConfigDto
    {
        /// <summary>配置ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>配置名称</summary>
        [Required(ErrorMessage = "配置名称不能为空")]
        [StringLength(100, ErrorMessage = "配置名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>用途类型：Realtime=实时/业务库，Historical=历史库</summary>
        [StringLength(20, ErrorMessage = "用途类型不能超过20个字符")]
        public string Type { get; set; } = "Historical";

        /// <summary>后端类型：MySQL / InfluxDB / PostgreSQL / SQLite</summary>
        [Required(ErrorMessage = "后端类型不能为空")]
        [StringLength(20, ErrorMessage = "后端类型不能超过20个字符")]
        public string BackendType { get; set; } = "InfluxDB";

        /// <summary>主机地址（IP 或域名）</summary>
        [StringLength(200, ErrorMessage = "主机地址不能超过200个字符")]
        public string Host { get; set; } = string.Empty;

        /// <summary>端口号</summary>
        [Range(1, 65535, ErrorMessage = "端口号必须在1到65535之间")]
        public int Port { get; set; }

        /// <summary>数据库用户名</summary>
        [StringLength(100, ErrorMessage = "用户名不能超过100个字符")]
        public string Username { get; set; } = string.Empty;

        /// <summary>密码（回显为掩码；保存时掩码/空 = 不改密）</summary>
        public string? Password { get; set; }

        /// <summary>是否已配置密码（仅回显用）</summary>
        public bool HasPassword { get; set; }

        /// <summary>数据库名称</summary>
        [StringLength(200, ErrorMessage = "数据库名称不能超过200个字符")]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>访问令牌（InfluxDB 2.x；回显为掩码）</summary>
        [StringLength(200, ErrorMessage = "令牌不能超过200个字符")]
        public string? Token { get; set; }

        /// <summary>是否已配置令牌（仅回显用）</summary>
        public bool HasToken { get; set; }

        /// <summary>组织名（InfluxDB 2.x）</summary>
        [StringLength(100, ErrorMessage = "组织名不能超过100个字符")]
        public string? Org { get; set; }

        /// <summary>Bucket 名称（InfluxDB 2.x）</summary>
        [StringLength(100, ErrorMessage = "Bucket 名称不能超过100个字符")]
        public string? Bucket { get; set; }

        /// <summary>是否当前生效（同 Type 唯一）</summary>
        public bool IsActive { get; set; }

        /// <summary>最近一次连接测试结果</summary>
        public string? LastStatus { get; set; }

        /// <summary>最近一次连接测试时间</summary>
        public DateTime? LastCheckedAt { get; set; }
    }

    /// <summary>
    /// 主库（MySQL）连接配置 DTO。
    /// <para>主库为系统自举依赖，配置存放于 appsettings + override 文件（非 DatabaseConfigs 表），
    /// 避免“用主库配置主库”的自举循环。</para>
    /// </summary>
    public class MainDatabaseConfigDto
    {
        /// <summary>主机地址（IP 或域名）</summary>
        [Required(ErrorMessage = "主机地址不能为空")]
        [StringLength(200, ErrorMessage = "主机地址不能超过200个字符")]
        public string Host { get; set; } = string.Empty;

        /// <summary>端口号</summary>
        [Range(1, 65535, ErrorMessage = "端口号必须在1到65535之间")]
        public int Port { get; set; }

        /// <summary>数据库名称</summary>
        [Required(ErrorMessage = "数据库名称不能为空")]
        [StringLength(200, ErrorMessage = "数据库名称不能超过200个字符")]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>数据库用户名</summary>
        [StringLength(100, ErrorMessage = "用户名不能超过100个字符")]
        public string Username { get; set; } = string.Empty;

        /// <summary>密码（回显为掩码；保存时掩码/空 = 不改密）</summary>
        public string? Password { get; set; }

        /// <summary>是否已配置密码</summary>
        public bool HasPassword { get; set; }
    }

    /// <summary>
    /// 数据库连接测试请求（主库与历史库通用）。
    /// </summary>
    public class TestConnectionRequest
    {
        /// <summary>后端类型：MySQL / InfluxDB 等</summary>
        [Required(ErrorMessage = "后端类型不能为空")]
        [StringLength(20, ErrorMessage = "后端类型不能超过20个字符")]
        public string BackendType { get; set; } = string.Empty;

        /// <summary>主机地址（IP 或域名）</summary>
        [StringLength(200, ErrorMessage = "主机地址不能超过200个字符")]
        public string Host { get; set; } = string.Empty;

        /// <summary>端口号</summary>
        [Range(1, 65535, ErrorMessage = "端口号必须在1到65535之间")]
        public int Port { get; set; }

        /// <summary>数据库用户名</summary>
        [StringLength(100, ErrorMessage = "用户名不能超过100个字符")]
        public string Username { get; set; } = string.Empty;

        /// <summary>数据库密码</summary>
        [StringLength(128, ErrorMessage = "密码不能超过128个字符")]
        public string Password { get; set; } = string.Empty;

        /// <summary>数据库名称</summary>
        [StringLength(200, ErrorMessage = "数据库名称不能超过200个字符")]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>访问令牌（InfluxDB 2.x）</summary>
        [StringLength(200, ErrorMessage = "令牌不能超过200个字符")]
        public string? Token { get; set; }

        /// <summary>组织名（InfluxDB 2.x）</summary>
        [StringLength(100, ErrorMessage = "组织名不能超过100个字符")]
        public string? Org { get; set; }

        /// <summary>Bucket 名称（InfluxDB 2.x）</summary>
        [StringLength(100, ErrorMessage = "Bucket 名称不能超过100个字符")]
        public string? Bucket { get; set; }
    }

    /// <summary>
    /// 数据库连接测试结果。
    /// </summary>
    public class TestConnectionResult
    {
        /// <summary>连接测试是否成功</summary>
        public bool Success { get; set; }

        /// <summary>连接往返延迟（毫秒）</summary>
        public long LatencyMs { get; set; }

        /// <summary>测试结果提示信息（成功或失败原因）</summary>
        public string Message { get; set; } = string.Empty;
    }
}
