namespace ScadaServer.Application.Options
{
    /// <summary>
    /// 系统日志采集与保留配置（appsettings.json 的 "SystemLog" 节）。
    /// </summary>
    public class SystemLogOptions
    {
        public const string SectionName = "SystemLog";

        /// <summary>
        /// 运行日志写库的最低级别（默认 Information），低于该级别丢弃
        /// </summary>
        public string MinLevel { get; set; } = "Information";

        /// <summary>
        /// 日志内容截断长度（默认 2000 字符）
        /// </summary>
        public int MaxContentLength { get; set; } = 2000;

        /// <summary>
        /// 类别前缀黑名单（如 "Microsoft."、"System.Net."），匹配即不写库
        /// </summary>
        public List<string> IgnoreCategories { get; set; } = new();

        /// <summary>
        /// 类别全名黑名单（如 "ScadaServer.WebApi.HostedServices.SystemLogRecorder"，防递归）
        /// </summary>
        public List<string> IgnoreExactCategories { get; set; } = new();

        /// <summary>
        /// 各分类日志保留天数（自动清理任务使用）
        /// </summary>
        public RetentionOptions Retention { get; set; } = new();
    }

    /// <summary>
    /// 各分类日志保留期
    /// </summary>
    public class RetentionOptions
    {
        public int Runtime { get; set; } = 30;
        public int Operation { get; set; } = 180;
        public int Security { get; set; } = 365;
    }
}
