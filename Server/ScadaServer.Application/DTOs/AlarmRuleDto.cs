using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    public class AlarmRuleDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "规则名称不能为空")]
        [StringLength(100, ErrorMessage = "名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "必须指定设备")]
        public int DeviceId { get; set; }

        [Required(ErrorMessage = "变量键不能为空")]
        [StringLength(50, ErrorMessage = "变量键不能超过50个字符")]
        public string VariableKey { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TriggerConditionEnum Condition { get; set; }

        public double Threshold { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AlarmLevelEnum Level { get; set; }

        public bool Active { get; set; } = true;

        [StringLength(500, ErrorMessage = "报警文案不能超过500个字符")]
        public string? Message { get; set; }

        [Range(0, 86400, ErrorMessage = "防抖秒数需在 0-86400 之间")]
        public int DebounceSeconds { get; set; }
    }
}
