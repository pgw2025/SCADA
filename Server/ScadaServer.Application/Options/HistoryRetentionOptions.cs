namespace ScadaServer.Application.Options
{
    /// <summary>
    /// 历史数据保留配置（appsettings.json 的 "History" 节）。
    /// </summary>
    public class HistoryRetentionOptions
    {
        /// <summary>配置节名称（appsettings.json 顶层键）</summary>
        public const string SectionName = "History";

        /// <summary>
        /// MySQL 历史数据保留天数（自动清理任务使用）。默认 0 = 不启用清理。
        /// <para>启用前必须先完成 MySQL → Influx 迁移（否则未迁移数据会被清掉）。</para>
        /// </summary>
        public int MySqlRetentionDays { get; set; } = 0;
    }
}
