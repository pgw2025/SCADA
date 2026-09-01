using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 定时任务实体
    /// </summary>
    [Table("ScheduledTasks")]
    public class ScheduledTask : EntityBase
    {
        /// <summary>
        /// 任务名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 任务类型（取值见 Enums.ScheduledTaskTypes 常量：set_value / backup / execute_script / clear_history）
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Cron 表达式，用于定义任务执行时间
        /// </summary>
        public string CronExpression { get; set; } = string.Empty;

        /// <summary>
        /// 任务参数（JSON格式）
        /// </summary>
        [Column(TypeName = "text")]
        public string ParamsJson { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// 最近一次执行开始时间（UTC 存储）
        /// </summary>
        public DateTime? LastRunAt { get; set; }

        /// <summary>
        /// 最近一次执行状态：Idle（初始，尚未执行）/ Running / Success / Failed / Skipped
        /// </summary>
        [MaxLength(16)]
        public string LastStatus { get; set; } = "Idle";

        /// <summary>
        /// 最近一次执行错误信息（执行失败时供前端展示）
        /// </summary>
        [MaxLength(2000)]
        public string? LastError { get; set; }

        /// <summary>
        /// 最近一次执行耗时（毫秒）
        /// </summary>
        public int? LastDurationMs { get; set; }

        /// <summary>
        /// 下次计划触发时间（UTC 存储，由调度器维护，前端展示用）
        /// </summary>
        public DateTime? NextRunAt { get; set; }
    }
}