using System.Text.Json.Serialization;

namespace ScadaServer.Application.DTOs
{
    #region 协议配置类（用于 JSON 序列化/反序列化）

    /// <summary>
    /// S7 协议配置
    /// </summary>
    public class S7Config
    {
        /// <summary>PLC 的 IP 地址</summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>PLC 端口号，默认 102</summary>
        public int Port { get; set; } = 102;

        /// <summary>机架号，默认 0</summary>
        public int Rack { get; set; } = 0;

        /// <summary>槽位号，默认 1</summary>
        public int Slot { get; set; } = 1;

        /// <summary>CPU 类型（如 S71500），默认 S71500</summary>
        public string CpuType { get; set; } = "S71500";

        /// <summary>
        /// IO 操作（读/写 PDU 往返）超时（毫秒），取值 500~60000，缺省 5000。
        /// 超时会中止当前连接（异步 socket 无法优雅取消，由既有重连机制恢复）。
        /// 注：S7netplus 的 Plc.ReadTimeout/WriteTimeout 仅对同步操作生效，异步路径必须经此超时约束。
        /// </summary>
        public int? IoTimeoutMs { get; set; }

        /// <summary>建链超时（毫秒），取值 500~60000，缺省 5000。</summary>
        public int? ConnectTimeoutMs { get; set; }
    }

    /// <summary>
    /// Modbus TCP 协议配置
    /// </summary>
    public class ModbusTcpConfig
    {
        /// <summary>目标设备 IP 地址</summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>端口号，默认 502</summary>
        public int Port { get; set; } = 502;

        /// <summary>从站单元地址（Unit ID），默认 1</summary>
        public byte UnitId { get; set; } = 1;
    }

    /// <summary>
    /// OPC UA 协议配置
    /// </summary>
    public class OpcUaConfig
    {
        /// <summary>OPC UA 服务器端点地址</summary>
        public string EndpointUrl { get; set; } = string.Empty;

        /// <summary>安全策略（如 None / Basic256Sha256），默认 None</summary>
        public string SecurityPolicy { get; set; } = "None";

        /// <summary>可空用户名（启用认证时使用）</summary>
        public string? Username { get; set; }

        /// <summary>可空密码（启用认证时使用）</summary>
        public string? Password { get; set; }
    }

    /// <summary>
    /// MQTT 协议配置
    /// </summary>
    public class MqttConfig
    {
        /// <summary>Broker 地址（如 tcp://localhost），MQTT 连接目标</summary>
        public string Broker { get; set; } = string.Empty;

        /// <summary>端口号，默认 1883</summary>
        public int Port { get; set; } = 1883;

        /// <summary>可空用户名（需要认证时使用）</summary>
        public string? Username { get; set; }

        /// <summary>可空密码（需要认证时使用）</summary>
        public string? Password { get; set; }

        /// <summary>订阅/发布主题</summary>
        public string Topic { get; set; } = string.Empty;

        /// <summary>MQTT 客户端 ID</summary>
        public string ClientId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 虚拟设备配置
    /// </summary>
    public class VirtualConfig
    {
        /// <summary>值更新间隔（毫秒），默认 1000</summary>
        public int IntervalMs { get; set; } = 1000;

        /// <summary>是否随机产生数值，默认 true</summary>
        public bool RandomValues { get; set; } = true;
    }

    #endregion
}
