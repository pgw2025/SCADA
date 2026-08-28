namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 历史数据迁移任务结果。
    /// <para>将 MySQL 存量历史数据一次性迁移写入当前生效的 InfluxDB 历史库。</para>
    /// </summary>
    public class HistoryMigrationResult
    {
        /// <summary>任务是否已启动/正在执行</summary>
        public bool IsRunning { get; set; }

        /// <summary>MySQL 存量历史记录总数</summary>
        public long Total { get; set; }

        /// <summary>本次成功写入 InfluxDB 的记录数（被跳过/失败的除外）</summary>
        public long Migrated { get; set; }

        /// <summary>结果说明</summary>
        public string Message { get; set; } = string.Empty;
    }
}