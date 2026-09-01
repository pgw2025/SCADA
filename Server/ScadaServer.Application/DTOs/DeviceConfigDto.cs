using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 设备协议配置 DTO
    /// </summary>
    public class DeviceConfigDto
    {
        public int DeviceId { get; set; }
        public string JsonConfig { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 创建/更新设备协议配置 DTO
    /// </summary>
    public class CreateDeviceConfigDto
    {
        [Required(ErrorMessage = "设备ID不能为空")]
        public int DeviceId { get; set; }

        [Required(ErrorMessage = "配置内容不能为空")]
        public string JsonConfig { get; set; } = string.Empty;
    }

    #region 协议配置类（用于 JSON 序列化/反序列化）

    /// <summary>
    /// S7 协议配置
    /// </summary>
    public class S7Config
    {
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 102;
        public int Rack { get; set; } = 0;
        public int Slot { get; set; } = 1;
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
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 502;
        public byte UnitId { get; set; } = 1;
    }

    /// <summary>
    /// OPC UA 协议配置
    /// </summary>
    public class OpcUaConfig
    {
        public string EndpointUrl { get; set; } = string.Empty;
        public string SecurityPolicy { get; set; } = "None";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    /// <summary>
    /// MQTT 协议配置
    /// </summary>
    public class MqttConfig
    {
        public string Broker { get; set; } = string.Empty;
        public int Port { get; set; } = 1883;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string Topic { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 虚拟设备配置
    /// </summary>
    public class VirtualConfig
    {
        public int IntervalMs { get; set; } = 1000;
        public bool RandomValues { get; set; } = true;
    }

    #endregion
}
