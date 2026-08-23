using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 系统配置实体
    /// </summary>
    [SugarTable("SystemConfig")]
    public class SystemConfig : EntityBase
    {
        /// <summary>
        /// 系统标题
        /// </summary>
        public string SystemTitle { get; set; }

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        public int PollIntervalMs { get; set; }

        /// <summary>
        /// MQTT Broker 地址
        /// </summary>
        public string MqttBrokerHost { get; set; }

        /// <summary>
        /// 数据保留周期（天）
        /// </summary>
        public int RetentionPeriodDays { get; set; }
    }
}