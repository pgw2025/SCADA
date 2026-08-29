namespace ScadaServer.Application.DTOs
{
    public class ScheduledTaskDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string CronExpression { get; set; } = string.Empty;
        public string ParamsJson { get; set; } = string.Empty;
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
