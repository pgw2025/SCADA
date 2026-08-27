using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 报警规则实体（从原 VariableTrigger 拆分而来，仅承载报警语义）
    /// </summary>
    [Table("AlarmRules")]
    public class AlarmRule : EntityBase
    {
        /// <summary>
        /// 规则名称
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 关联的设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 监控的变量键
        /// </summary>
        [MaxLength(50)]
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 触发条件（枚举，替代原 string Condition）
        /// </summary>
        public TriggerConditionEnum Condition { get; set; }

        /// <summary>
        /// 阈值
        /// </summary>
        public double Threshold { get; set; }

        /// <summary>
        /// 报警级别（枚举，替代原 string AlarmLevel / Severity）
        /// </summary>
        public AlarmLevelEnum Level { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// 报警文案（为空时使用默认模板"变量名 条件 阈值"）
        /// </summary>
        [MaxLength(500)]
        public string? Message { get; set; }

        /// <summary>
        /// 防抖秒数（默认 0）：进入报警后该秒数内恢复视为抖动，不产生报警事件。
        /// </summary>
        public int DebounceSeconds { get; set; }
    }
}
