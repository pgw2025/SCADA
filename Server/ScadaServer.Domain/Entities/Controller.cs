using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 控制器实体（Controller/PLC 资产台账，阶段 2 引入）。
    /// <para>
    /// 承接目标设计中"物理控制硬件资产"（S7-1500 PLC、Kepware 服务器等）。
    /// 当前阶段仅作资产登记（只加表、加页面、不接线）：Device 与本表暂无任何外键关系，
    /// 不产生任何采集行为；运行连接配置（DeviceConnection）在后续阶段接入。
    /// </para>
    /// <para>类型（PLC/OPCUA Server）直接落地为 <see cref="ProtocolId"/> FK → <see cref="Protocol"/>（协议即控制器类型）。</para>
    /// </summary>
    [Table("Controllers")]
    public class Controller
    {
        /// <summary>主键ID，自增字段。</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>控制器编码（业务键，全局唯一）。</summary>
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        /// <summary>控制器名称。</summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>控制器类型/所用协议（S7、OPCUA...），FK → Protocols。</summary>
        public int ProtocolId { get; set; }

        /// <summary>所属协议导航属性（协议即控制器类型）。</summary>
        [ForeignKey(nameof(ProtocolId))]
        public Protocol? Protocol { get; set; }

        /// <summary>厂商（Siemens/Kepware...）。</summary>
        [MaxLength(100)]
        public string? Manufacturer { get; set; }

        /// <summary>型号（S7-1500/KEPServerEX...）。</summary>
        [MaxLength(100)]
        public string? Model { get; set; }

        /// <summary>描述。</summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>是否启用（禁用后不可被后续连接引用）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 该控制器下的连接集合（阶段 3 起：DeviceConnection.ControllerId FK → 本表）。
        /// 过渡期每连接独占一个控制器；多设备共享控制器的演进由管理界面合并。
        /// </summary>
        public List<DeviceConnection> Connections { get; set; } = new();

        /// <summary>创建时间（UTC 存储）。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC 存储）。</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
