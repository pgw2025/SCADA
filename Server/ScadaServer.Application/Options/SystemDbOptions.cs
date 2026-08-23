namespace ScadaServer.Application.Options
{
    /// <summary>
    /// 系统数据库配置选项
    /// </summary>
    public class SystemDbOptions
    {
        /// <summary>
        /// 配置节名称
        /// </summary>
        public const string SectionName = "SystemDbConfig";

        /// <summary>
        /// 数据库主机地址
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// 数据库端口
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 数据库名称
        /// </summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 生成MySQL连接字符串
        /// </summary>
        /// <returns>连接字符串</returns>
        public string GetConnectionString() =>
            $"Server={Host};Port={Port};Database={DatabaseName};Uid={Username};Pwd={Password};";
    }
}
