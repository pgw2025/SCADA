using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 报警记录 DTO（查询/返回给前端，含触发/恢复/确认状态）。
    /// </summary>
    public class AlarmRecordDto
    {
        /// <summary>报警记录唯一标识（主键）</summary>
        public long Id { get; set; }

        /// <summary>所属设备ID</summary>
        public int DeviceId { get; set; }

        /// <summary>设备业务标识（字符串）</summary>
        public string DeviceKey { get; set; } = string.Empty;

        /// <summary>告警变量业务键</summary>
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>关联的数据点模板ID（DataPoint.Id；存量未回填或匹配不上的记录为 NULL）</summary>
        public int? DataPointId { get; set; }

        /// <summary>告警变量名称</summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>命中的规则ID（规则告警有值；兜底告警为空）</summary>
        public long? RuleId { get; set; }

        /// <summary>规则名称（可空）</summary>
        public string? RuleName { get; set; }

        /// <summary>报警级别；以字符串枚举序列化供前端阅读</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AlarmLevelEnum Level { get; set; }

        /// <summary>触发的比较条件（可空）；以字符串枚举序列化</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TriggerConditionEnum? Condition { get; set; }

        /// <summary>触发阈值（规则告警有值）</summary>
        public double? Threshold { get; set; }

        /// <summary>触发时实际值（字符串，可空）</summary>
        public string? ActualValue { get; set; }

        /// <summary>报警文案</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>报警来源（Rule / MinMaxLimit / System）；以字符串枚举序列化</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AlarmSourceEnum Source { get; set; }

        /// <summary>触发时间</summary>
        public DateTime TriggeredAt { get; set; }

        /// <summary>恢复时间（未恢复为空）</summary>
        public DateTime? RecoveredAt { get; set; }

        /// <summary>恢复时的实际值（可空）</summary>
        public string? RecoveryValue { get; set; }

        /// <summary>是否已确认</summary>
        public bool Acked { get; set; }

        /// <summary>确认时间（未确认为空）</summary>
        public DateTime? AckedAt { get; set; }

        /// <summary>确认人（可空）</summary>
        public string? AckedBy { get; set; }
    }
}