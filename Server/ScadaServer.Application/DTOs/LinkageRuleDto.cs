using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 联动规则 DTO（条件触发后将指定值写入目标变量，实现设备间联动）。
    /// </summary>
    public class LinkageRuleDto
    {
        /// <summary>规则ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>规则名称；必填，最长 100 字符（校验特性）</summary>
        [Required(ErrorMessage = "规则名称不能为空")]
        [StringLength(100, ErrorMessage = "名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>触发源设备ID；必填，需大于 0（校验特性）</summary>
        [Range(1, int.MaxValue, ErrorMessage = "必须指定设备")]
        public int DeviceId { get; set; }

        /// <summary>触发源变量键；必填，最长 50 字符（校验特性）</summary>
        [Required(ErrorMessage = "变量键不能为空")]
        [StringLength(50, ErrorMessage = "变量键不能超过50个字符")]
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>触发比较条件；以字符串枚举序列化</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TriggerConditionEnum Condition { get; set; }

        /// <summary>触发阈值</summary>
        public double Threshold { get; set; }

        /// <summary>联动动作类型（写入值/重启等）；以字符串枚举序列化</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public LinkageActionEnum ActionType { get; set; }

        /// <summary>联动目标变量键；必填，最长 50 字符（校验特性）</summary>
        [Required(ErrorMessage = "联动目标变量键不能为空")]
        [StringLength(50, ErrorMessage = "变量键不能超过50个字符")]
        public string LinkageVariableKey { get; set; } = string.Empty;

        /// <summary>写入目标变量的联动值；最长 200 字符（校验特性）</summary>
        [StringLength(200, ErrorMessage = "联动值不能超过200个字符")]
        public string LinkageValue { get; set; } = string.Empty;

        /// <summary>是否启用该联动规则；默认启用</summary>
        public bool Active { get; set; } = true;
    }
}
