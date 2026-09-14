namespace ScadaServer.Application.Options
{
    /// <summary>
    /// 历史记录器配置（appsettings.json 的 "HistoryRecorder" 节）。
    /// </summary>
    public class HistoryRecorderOptions
    {
        /// <summary>配置节名称（appsettings.json 顶层键）</summary>
        public const string SectionName = "HistoryRecorder";

        /// <summary>主缓冲队列容量（条）。队列满时 DropWrite 丢弃并计数告警。</summary>
        public int ChannelCapacity { get; set; } = 20000;

        /// <summary>批量落库条数。</summary>
        public int FlushBatchSize { get; set; } = 100;

        /// <summary>批次累积窗口（毫秒）：未满批也按时落库。</summary>
        public int FlushIntervalMs { get; set; } = 500;

        /// <summary>内存补偿队列容量（条）。</summary>
        public int RetryBufferCapacity { get; set; } = 50000;

        /// <summary>补偿批次连续失败放弃轮数上限（防毒丸批次永久占用）。</summary>
        public int MaxRetryRounds { get; set; } = 10;

        /// <summary>
        /// 是否启用补偿落盘（WAL）。启用后：内存补偿队列溢出、停止时滞留的批次会暂存磁盘，
        /// 进程重启后启动重放，避免双后端故障期间的历史数据随进程退出丢失。
        /// </summary>
        public bool RetrySpillEnabled { get; set; } = true;

        /// <summary>补偿落盘目录（相对 ContentRoot；空则不落盘）。</summary>
        public string RetrySpillDirectory { get; set; } = "history-retry";

        /// <summary>补偿落盘目录最大字节数（默认 512MB），超限从最旧文件开始清理。</summary>
        public long RetrySpillMaxBytes { get; set; } = 512L * 1024 * 1024;
    }
}