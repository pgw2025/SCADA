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
        public string SystemTitle { get; set; } = string.Empty;

        /// <summary>前端轮询间隔（毫秒）</summary>
        public int PollIntervalMs { get; set; }

        /// <summary>MQTT Broker 主机地址</summary>
        public string MqttBrokerHost { get; set; } = string.Empty;

        /// <summary>历史数据保留周期（天）</summary>
        public int RetentionPeriodDays { get; set; }
    }
}
