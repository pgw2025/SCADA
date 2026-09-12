namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 历史写入运行期统计快照（只读，供状态/统计接口读取，Interlocked 计数保证零锁读取）。
    /// </summary>
    public class HistoryRecorderStats
    {
        /// <summary>当前队列剩余容量（Channel 内待写条目数）</summary>
        public int QueueDepth { get; set; }

        /// <summary>累计入队采样点数</summary>
        public long EnqueuedTotal { get; set; }

        /// <summary>队列满丢弃采样点数</summary>
        public long DroppedQueueFull { get; set; }

        /// <summary>双后端重试穷尽且补偿队列溢出丢弃的采样点数</summary>
        public long DroppedAllBackendFailed { get; set; }

        /// <summary>InfluxDB 成功写入批次数</summary>
        public long InfluxWriteBatches { get; set; }

        /// <summary>InfluxDB 写入失败批次数（含重试后仍失败）</summary>
        public long InfluxWriteFailedBatches { get; set; }

        /// <summary>MySQL 成功写入批次数</summary>
        public long MysqlWriteBatches { get; set; }

        /// <summary>MySQL 写入重试批次数</summary>
        public long MysqlRetriedBatches { get; set; }

        /// <summary>NaN/Infinity 采样点分流计数（改道 MySQL，非丢弃）</summary>
        public long InvalidValuePoints { get; set; }

        /// <summary>补偿队列当前深度（条）</summary>
        public int RetryBufferDepth { get; set; }

        /// <summary>最近一次落库时间（UTC）</summary>
        public DateTime? LastFlushAt { get; set; }

        /// <summary>最近一次成功落库时间（UTC）</summary>
        public DateTime? LastWriteSucceededAt { get; set; }
    }

    /// <summary>
    /// 历史写入运行期统计提供者。
    /// <para>仅查询/运维侧依赖，采集层不依赖本接口，避免污染采集端。</para>
    /// </summary>
    public interface IHistoryRecorderStats
    {
        /// <summary>获取当前统计快照。</summary>
        HistoryRecorderStats GetStats();
    }
}
