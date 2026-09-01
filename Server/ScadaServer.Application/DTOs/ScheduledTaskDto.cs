namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 定时任务 DTO（定义按 cron 表达式周期执行的任务及其执行状态）。
    /// </summary>
    public class ScheduledTaskDto
    {
        /// <summary>任务ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>任务名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>任务类型（标识具体执行的业务动作）</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>触发计划 cron 表达式</summary>
        public string CronExpression { get; set; } = string.Empty;

        /// <summary>任务参数（JSON 串）</summary>
        public string ParamsJson { get; set; } = string.Empty;

        /// <summary>是否启用该任务</summary>
        public bool Active { get; set; }

        /// <summary>最近一次执行开始时间（UTC，前端本地化展示）</summary>
        public DateTime? LastRunAt { get; set; }

        /// <summary>最近一次执行状态：Idle / Running / Success / Failed / Skipped</summary>
        public string LastStatus { get; set; } = "Idle";

        /// <summary>最近一次执行错误信息</summary>
        public string? LastError { get; set; }

        /// <summary>最近一次执行耗时（毫秒）</summary>
        public int? LastDurationMs { get; set; }

        /// <summary>下次计划触发时间（UTC，前端本地化展示）</summary>
        public DateTime? NextRunAt { get; set; }
    }
}
