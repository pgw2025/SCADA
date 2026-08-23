using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// MQTT变量配置实体
    /// </summary>
    [SugarTable("MqttVariableConfigs")]
    public class MqttVariableConfig : EntityBase
    {
        /// <summary>
        /// 关联的MQTT服务器ID
        /// </summary>
        public int MqttServerId { get; set; }

        /// <summary>
        /// 关联的设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 变量键
        /// </summary>
        public string VariableKey { get; set; }

        /// <summary>
        /// 变量别名
        /// </summary>
        public string Alias { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 自定义MQTT主题（可选）
        /// </summary>
        public string? CustomTopic { get; set; }
    }
}