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
        public string Name { get; set; } = string.Empty;

        /// <summary>用途类型：Realtime=实时/业务库，Historical=历史库</summary>
        public string Type { get; set; } = "Historical";

        /// <summary>后端类型：MySQL / InfluxDB / PostgreSQL / SQLite</summary>
        public string BackendType { get; set; } = "InfluxDB";

        /// <summary>主机地址（IP 或域名）</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>端口号</summary>
        public int Port { get; set; }

        /// <summary>数据库用户名</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>密码（回显为掩码；保存时掩码/空 = 不改密）</summary>
        public string? Password { get; set; }

        /// <summary>是否已配置密码（仅回显用）</summary>
        public bool HasPassword { get; set; }

        /// <summary>数据库名称</summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>访问令牌（InfluxDB 2.x；回显为掩码）</summary>
        public string? Token { get; set; }

        /// <summary>是否已配置令牌（仅回显用）</summary>
        public bool HasToken { get; set; }

        /// <summary>组织名（InfluxDB 2.x）</summary>
        public string? Org { get; set; }

        /// <summary>Bucket 名称（InfluxDB 2.x）</summary>
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
        public string Host { get; set; } = string.Empty;

        /// <summary>端口号</summary>
        public int Port { get; set; }

        /// <summary>数据库名称</summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>数据库用户名</summary>
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
        public string BackendType { get; set; } = string.Empty;

        /// <summary>主机地址（IP 或域名）</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>端口号</summary>
        public int Port { get; set; }

        /// <summary>数据库用户名</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>数据库密码</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>数据库名称</summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>访问令牌（InfluxDB 2.x）</summary>
        public string? Token { get; set; }

        /// <summary>组织名（InfluxDB 2.x）</summary>
        public string? Org { get; set; }

        /// <summary>Bucket 名称（InfluxDB 2.x）</summary>
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
