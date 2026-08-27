using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 报警记录 DTO（查询/返回给前端，含触发/恢复/确认状态）。
    /// </summary>
    public class AlarmRecordDto
    {
        public long Id { get; set; }

        public int DeviceId { get; set; }

        public string DeviceKey { get; set; } = string.Empty;

        public string VariableKey { get; set; } = string.Empty;

        public string VariableName { get; set; } = string.Empty;

        public long? RuleId { get; set; }

        public string? RuleName { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AlarmLevelEnum Level { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TriggerConditionEnum? Condition { get; set; }

        public double? Threshold { get; set; }

        public string? ActualValue { get; set; }

        public string Message { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AlarmSourceEnum Source { get; set; }

        public DateTime TriggeredAt { get; set; }

        public DateTime? RecoveredAt { get; set; }

        public string? RecoveryValue { get; set; }

        public bool Acked { get; set; }

        public DateTime? AckedAt { get; set; }

        public string? AckedBy { get; set; }
    }
}