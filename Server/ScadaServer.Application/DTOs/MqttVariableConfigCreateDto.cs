using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 新增 MQTT 变量映射请求体。
    /// </summary>
    public class MqttVariableConfigCreateDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "必须指定设备")]
        public int DeviceId { get; set; }

        [Required(ErrorMessage = "变量键不能为空")]
        [StringLength(100, ErrorMessage = "变量键不能超过100个字符")]
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 转发别名（必填，默认由前端带入原变量名）；同一变量可绑定多台服务器且别名各自独立。
        /// </summary>
        [Required(ErrorMessage = "转发别名不能为空")]
        [StringLength(100, ErrorMessage = "别名不能超过100个字符")]
        public string Alias { get; set; } = string.Empty;

        /// <summary>
        /// 自定义主题（可选）；非空时优先于「服务器前缀/别名」拼接。
        /// </summary>
        [StringLength(200, ErrorMessage = "自定义主题不能超过200个字符")]
        public string? CustomTopic { get; set; }
    }

    /// <summary>
    /// 更新 MQTT 变量映射请求体（别名/自定义主题/启用开关）。
    /// </summary>
    public class MqttVariableConfigUpdateDto
    {
        [Required(ErrorMessage = "转发别名不能为空")]
        [StringLength(100, ErrorMessage = "别名不能超过100个字符")]
        public string Alias { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "自定义主题不能超过200个字符")]
        public string? CustomTopic { get; set; }

        public bool IsEnabled { get; set; } = true;
    }
}