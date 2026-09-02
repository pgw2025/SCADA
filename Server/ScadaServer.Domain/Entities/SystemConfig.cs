using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 系统配置实体
    /// </summary>
    [Table("SystemConfig")]
    public class SystemConfig
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 系统标题
        /// </summary>
        public string SystemTitle { get; set; } = string.Empty;

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        public int PollIntervalMs { get; set; }

        /// <summary>
        /// MQTT Broker 地址
        /// </summary>
        public string MqttBrokerHost { get; set; } = string.Empty;

        /// <summary>
        /// 数据保留周期（天）
        /// </summary>
        public int RetentionPeriodDays { get; set; }
    }
}