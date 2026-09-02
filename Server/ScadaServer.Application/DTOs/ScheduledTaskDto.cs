using System.ComponentModel.DataAnnotations;

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
        [Required(ErrorMessage = "任务名称不能为空")]
        [StringLength(100, ErrorMessage = "任务名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>任务类型（标识具体执行的业务动作）</summary>
        [Required(ErrorMessage = "任务类型不能为空")]
        [StringLength(50, ErrorMessage = "任务类型不能超过50个字符")]
        public string Type { get; set; } = string.Empty;

        /// <summary>触发计划 cron 表达式</summary>
        [Required(ErrorMessage = "Cron 表达式不能为空")]
        [StringLength(30, ErrorMessage = "Cron 表达式不能超过30个字符")]
        public string CronExpression { get; set; } = string.Empty;

        /// <summary>任务参数（JSON 串）</summary>
        [StringLength(3000, ErrorMessage = "任务参数不能超过3000个字符")]
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
