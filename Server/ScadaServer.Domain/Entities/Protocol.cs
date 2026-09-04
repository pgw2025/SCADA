using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 通信协议实体，描述系统所支持的通信方式（如 Siemens S7、OPC UA、虚拟设备等）。
    /// <para>
    /// 引入本实体的目的在于将"通信协议"从 <see cref="DataModel.Type"/>（原 <c>DeviceType</c> 枚举，
    /// 同时承担"设备型号"与"通信协议"两个职责）中剥离为一阶的独立概念：
    /// <see cref="DataModel"/> 只负责"设备型号"，<see cref="Protocol"/> 只负责"通信方式"。
    /// </para>
    /// <para>本协议只定义"是什么 / 怎么派发"，不承载任何采集运行逻辑（运行期与驱动实现保持不变）。</para>
    /// </summary>
    [Table("Protocols")]
    public class Protocol
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 协议业务键（稳定标识符，如 "s7"、"opcua"、"virtual"）。
        /// 用于驱动工厂等派发逻辑按 Key 定位协议，建议与代码中的协议常量保持一致，且全局唯一。
        /// </summary>
        [MaxLength(50)]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// 协议显示名称（如 "Siemens S7"、"OPC UA"），用于界面展示。
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 协议描述信息，可用于备注厂商、适用场景等。
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 是否启用。禁用后运行期不应基于此协议创建驱动实例；用于在不删除数据的前提下临时停用某协议。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 创建时间（UTC 存储）。
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 更新时间（UTC 存储）。
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
