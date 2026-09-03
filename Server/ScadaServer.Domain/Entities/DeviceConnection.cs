using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 设备连接实体（阶段 3 引入）：把"设备怎么连接"从 <see cref="Device.JsonConfig"/> 中抽取为独立实体。
    /// <para>
    /// 一台 <see cref="Device"/> 经 <see cref="Device.ConnectionId"/> 指向本实体；过渡期采用
    /// "1 设备 = 1 独占 Controller + 1 DeviceConnection"（P3-A），与重构前 JsonConfig 行为 100% 等价；
    /// 多设备共享同一连接是管理界面后续手工合并的演进方向，本阶段不做运行时共享语义。
    /// </para>
    /// <para>
    /// <see cref="ConfigJson"/> 保存驱动完整配置原文（即原 JsonConfig，P3-B），
    /// <see cref="Host"/> / <see cref="Port"/> 为额外提取的冗余列（管理/检索用），不做运行真相源。
    /// 运行时优先读取本实体的 <see cref="ConfigJson"/>，回退 <see cref="Device.JsonConfig"/>（双读兼容层）。
    /// </para>
    /// </summary>
    [Table("DeviceConnections")]
    public class DeviceConnection
    {
        /// <summary>主键ID，自增字段。</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>所属控制器 ID（FK → Controllers，Restrict），过渡期每连接独占一个控制器。</summary>
        public int ControllerId { get; set; }

        /// <summary>所属控制器导航属性。</summary>
        [ForeignKey(nameof(ControllerId))]
        public Controller? Controller { get; set; }

        /// <summary>连接名称。</summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>协议（S7/OPCUA/Virtual...），FK → Protocols（Restrict）。</summary>
        public int ProtocolId { get; set; }

        /// <summary>协议导航属性（驱动派发真相源）。</summary>
        [ForeignKey(nameof(ProtocolId))]
        public Protocol? Protocol { get; set; }

        /// <summary>
        /// 提取的 IP / 主机名（S7 = IpAddress；OPC UA = 端点 URL 主机；Virtual = NULL）。
        /// 冗余列，仅供管理/检索展示，不参与运行时连接。
        /// </summary>
        [MaxLength(100)]
        public string? Host { get; set; }

        /// <summary>提取的端口（S7 = Port；OPC UA = 端点端口；Virtual = NULL）。冗余列，不参与运行时连接。</summary>
        public int? Port { get; set; }

        /// <summary>
        /// 驱动完整配置原文（即原 Device.JsonConfig，含 IP/端口/端点等，P3-B）。
        /// 运行时优先取本字段反序列化为驱动配置，保持"逐字节等价"。
        /// </summary>
        [Column(TypeName = "longtext")]
        public string? ConfigJson { get; set; }

        /// <summary>IO 超时（毫秒），回填时取 S7 IoTimeoutMs，无则默认 5000。</summary>
        public int TimeoutMs { get; set; } = 5000;

        /// <summary>重连周期（毫秒），回填默认 5000（与现运行时行为一致）。</summary>
        public int ReconnectIntervalMs { get; set; } = 5000;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC 存储）。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC 存储）。</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
