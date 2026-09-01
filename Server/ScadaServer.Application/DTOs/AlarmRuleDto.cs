using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 报警规则 DTO（定义变量触发条件、级别、阈值及告警文案）。
    /// </summary>
    public class AlarmRuleDto
    {
        /// <summary>规则ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>规则名称；必填，最长 100 字符（校验特性）</summary>
        [Required(ErrorMessage = "规则名称不能为空")]
        [StringLength(100, ErrorMessage = "名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>所属设备ID；必填，范围需大于 0（校验特性）</summary>
        [Range(1, int.MaxValue, ErrorMessage = "必须指定设备")]
        public int DeviceId { get; set; }

        /// <summary>告警变量业务键；必填，最长 50 字符（校验特性）</summary>
        [Required(ErrorMessage = "变量键不能为空")]
        [StringLength(50, ErrorMessage = "变量键不能超过50个字符")]
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>触发比较条件；以字符串枚举序列化</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TriggerConditionEnum Condition { get; set; }

        /// <summary>触发阈值（数值）</summary>
        public double Threshold { get; set; }

        /// <summary>报警级别；以字符串枚举序列化</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AlarmLevelEnum Level { get; set; }

        /// <summary>是否启用该规则；默认启用</summary>
        public bool Active { get; set; } = true;

        /// <summary>报警文案；可空，最长 500 字符（校验特性）</summary>
        [StringLength(500, ErrorMessage = "报警文案不能超过500个字符")]
        public string? Message { get; set; }

        /// <summary>防抖（去抖）秒数，0-86400，避免短时间重复告警（校验特性）</summary>
        [Range(0, 86400, ErrorMessage = "防抖秒数需在 0-86400 之间")]
        public int DebounceSeconds { get; set; }
    }
}
