namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 历史数据迁移任务结果（迁移触发/状态查询共用）。
    /// <para>将 MySQL 存量历史数据迁移写入当前生效的 InfluxDB 历史库，支持断点续传。</para>
    /// </summary>
    public class HistoryMigrationResult
    {
        /// <summary>任务是否已启动/正在执行（旧字段，兼容保留）</summary>
        public bool IsRunning { get; set; }

        /// <summary>MySQL 存量历史记录总数</summary>
        public long Total { get; set; }

        /// <summary>本次成功写入 InfluxDB 的记录数（被跳过/失败的除外）</summary>
        public long Migrated { get; set; }

        /// <summary>结果说明</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>迁移状态：NeverStarted / Running / Completed / Interrupted</summary>
        public string Status { get; set; } = "NeverStarted";

        /// <summary>当前断点（已迁移到的最后一条 MySQL 历史 Id），续传时从此 Id 之后继续</summary>
        public long LastId { get; set; }

        /// <summary>任务启动时间（UTC）</summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>最近一次进度更新时间（UTC）</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>当前迁移速率（条/秒），0 表示无法估算</summary>
        public double CurrentSpeedPerSec { get; set; }

        /// <summary>预计剩余时间（秒），0 表示无法估算</summary>
        public long EtaSeconds { get; set; }
    }
}
