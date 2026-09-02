using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 系统全局配置 DTO。
    /// </summary>
    public class SystemConfigDto
    {
        /// <summary>配置ID（主键）</summary>
        public int Id { get; set; }

        /// <summary>系统标题（界面显示名称）</summary>
        [Required(ErrorMessage = "系统标题不能为空")]
        [StringLength(100, ErrorMessage = "系统标题不能超过100个字符")]
        public string SystemTitle { get; set; } = string.Empty;

        /// <summary>前端轮询间隔（毫秒）</summary>
        [Range(100, 3600000, ErrorMessage = "轮询间隔必须在100ms到1小时之间")]
        public int PollIntervalMs { get; set; }

        /// <summary>MQTT Broker 主机地址</summary>
        [StringLength(200, ErrorMessage = "MQTT 主机地址不能超过200个字符")]
        public string MqttBrokerHost { get; set; } = string.Empty;

        /// <summary>历史数据保留周期（天）</summary>
        [Range(1, 3650, ErrorMessage = "保留周期必须在1到3650天之间")]
        public int RetentionPeriodDays { get; set; }
    }
}
