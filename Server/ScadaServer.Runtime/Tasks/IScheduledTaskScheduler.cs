namespace ScadaServer.Runtime.Tasks
{
    /// <summary>
    /// 定时任务调度器：负责加载 Cron 作业、按计划触发执行、维护执行状态并支持手动触发。
    /// <para>
    /// 生命周期为 Singleton，由宿主启动时 StartAsync，CRUD 后 ReloadAsync 重载生效配置；
    /// 每 10 分钟兜底重载一次，防止库内数据被外部修改后调度器长期失联。
    /// </para>
    /// </summary>
    public interface IScheduledTaskScheduler
    {
        /// <summary>
        /// 从数据库重载任务调度（新增/编辑/删除/启停后调用）。
        /// </summary>
        Task ReloadAsync();

        /// <summary>
        /// 手动执行任务（admin 操作，绕过 Cron 计划；仍受防重入保护）。
        /// 返回本次执行结果。
        /// </summary>
        Task<TaskRunResult> RunAsync(int taskId, string executedBy);
    }

    /// <summary>
    /// 任务单次执行结果（返回给调度器与前端）。
    /// </summary>
    public class TaskRunResult
    {
        public int TaskId { get; set; }

        /// <summary>执行状态：Success / Failed / Skipped</summary>
        public string Status { get; set; } = "Success";

        /// <summary>执行输出摘要（如备份文件路径、写入的变量值）</summary>
        public string? Output { get; set; }

        /// <summary>失败原因</summary>
        public string? Error { get; set; }

        /// <summary>耗时（毫秒）</summary>
        public int DurationMs { get; set; }
    }
}
