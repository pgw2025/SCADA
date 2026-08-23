using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// MQTT服务器实体
    /// </summary>
    [SugarTable("MqttServers")]
    public class MqttServer : EntityBase
    {
        /// <summary>
        /// 服务器名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Broker地址
        /// </summary>
        public string BrokerUrl { get; set; }

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 客户端ID
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 主题前缀
        /// </summary>
        public string TopicPrefix { get; set; }
    }
}