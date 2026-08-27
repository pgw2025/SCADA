using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 报警记录实体（告警流水/事件，含触发、恢复与确认状态）。
    /// <para>
    /// 由运行时报警检测（规则引擎命中或 Min/Max 上下限兜底）产生并经 AlarmRecorder
    /// 异步批量落库。前端据此查询未确认/未恢复告警列表，并执行确认操作。
    /// </para>
    /// </summary>
    [Table("AlarmRecords")]
    public class AlarmRecord
    {
        /// <summary>
        /// 主键（自增）
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// 所属设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 设备标识（冗余存储，便于按设备维度查询与展示）
        /// </summary>
        [MaxLength(64)]
        public string DeviceKey { get; set; } = string.Empty;

        /// <summary>
        /// 变量业务键（对应 VariableRuntime.Key）
        /// </summary>
        [MaxLength(64)]
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 变量名称（冗余存储，避免查询时再关联变量表）
        /// </summary>
        [MaxLength(100)]
        public string VariableName { get; set; } = string.Empty;

        /// <summary>
        /// 命中的报警规则ID（规则告警有值；Min/Max 上下限兜底为空）
        /// </summary>
        public long? RuleId { get; set; }

        /// <summary>
        /// 报警规则名称（冗余存储）
        /// </summary>
        [MaxLength(100)]
        public string? RuleName { get; set; }

        /// <summary>
        /// 报警级别（Low/Medium/High/Critical）
        /// </summary>
        public AlarmLevelEnum Level { get; set; }

        /// <summary>
        /// 触发的比较条件（规则告警有值；兜底告警为空）
        /// </summary>
        public TriggerConditionEnum? Condition { get; set; }

        /// <summary>
        /// 阈值（规则告警有值；兜底告警为空，由 Min/Max 决定）
        /// </summary>
        public double? Threshold { get; set; }

        /// <summary>
        /// 实际值字符串（统一字符串存储，避免历史发生不同类型冲突）
        /// </summary>
        [MaxLength(128)]
        public string? ActualValue { get; set; }

        /// <summary>
        /// 报警文案
        /// </summary>
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 触发来源（Rule 规则命中 / MinMaxLimit 上下限兜底 / System 系统级）
        /// </summary>
        public AlarmSourceEnum Source { get; set; }

        /// <summary>
        /// 触发时间
        /// </summary>
        public DateTime TriggeredAt { get; set; }

        /// <summary>
        /// 恢复时间（为空表示尚未恢复）
        /// </summary>
        public DateTime? RecoveredAt { get; set; }

        /// <summary>
        /// 恢复时的实际值
        /// </summary>
        [MaxLength(128)]
        public string? RecoveryValue { get; set; }

        /// <summary>
        /// 是否已确认
        /// </summary>
        public bool Acked { get; set; }

        /// <summary>
        /// 确认时间
        /// </summary>
        public DateTime? AckedAt { get; set; }

        /// <summary>
        /// 确认人
        /// </summary>
        [MaxLength(64)]
        public string? AckedBy { get; set; }
    }
}