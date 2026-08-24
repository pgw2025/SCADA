using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据模型实体，描述一类设备"是什么型号"，与"如何通信"解耦。
    /// <para>
    /// 协议真相源已从 <see cref="Type"/>（原 <c>DeviceType</c> 枚举，同时承担设备型号与通信协议）
    /// 迁移至独立的 <see cref="Protocol"/> 实体：本实体通过 <see cref="ProtocolId"/> 关联协议，
    /// <see cref="Type"/> 仅作为过渡期兼容字段保留（运行期 / 驱动仍依赖它派发驱动），
    /// 待运行期与驱动改造完成、统一改用 <see cref="Protocol"/> 后，<see cref="Type"/> 将被移除。
    /// </para>
    /// </summary>
    [Table("DataModels")]
    public class DataModel : EntityBase
    {
        /// <summary>
        /// 模型名称（业务可读的名称，如 "1500 主控"）。
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 设备厂商（如 "Siemens"、"Schneider"）。
        /// 与 <see cref="ModelName"/> 组合可完整表示设备型号，例如 Vendor="Siemens" + ModelName="S7-1500" → "Siemens S7-1500"。
        /// </summary>
        [MaxLength(100)]
        public string? Vendor { get; set; }

        /// <summary>
        /// 设备型号名称（如 "S7-1500"、"M340"）。
        /// </summary>
        [MaxLength(100)]
        public string? ModelName { get; set; }

        /// <summary>
        /// 厂商/型号描述（如 "Siemens S7-1500"、"Schneider M340"）。
        /// 仅作描述性信息，可由 <see cref="Vendor"/> 与 <see cref="ModelName"/> 拼接得出，也可手动填写。
        /// </summary>
        [MaxLength(100)]
        public string? VendorModel { get; set; }

        /// <summary>
        /// 模型描述
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 关联通信协议的外部键。协议真相源统一为 <see cref="Protocol"/>，
        /// 一台设备通过本模型绑定的协议确定其驱动方式；允许为空以兼容尚未指定协议的过渡数据。
        /// </summary>
        public int? ProtocolId { get; set; }

        /// <summary>
        /// 关联通信协议导航属性（对应 <see cref="ProtocolId"/>）。
        /// </summary>
        [ForeignKey(nameof(ProtocolId))]
        public Protocol? Protocol { get; set; }

        /// <summary>
        /// 过渡期兼容字段：协议类型（枚举）。
        /// <para>
        /// 运行期与驱动目前仍通过此字段派发驱动（<c>RuntimeManager</c> 调用
        /// <c>ProtocolDriverFactory.CreateDriver(model.Type)</c>），故暂时保留；
        /// 新逻辑应优先使用 <see cref="ProtocolId"/> / <see cref="Protocol"/>。
        /// 该字段将在运行期与驱动改造完成、统一迁移到 <see cref="Protocol"/> 后移除。
        /// </para>
        /// </summary>
        public DeviceType Type { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 模型包含的变量列表
        /// </summary>
        [NotMapped]
        public List<ModelVariable>? Variables { get; set; }
    }
}
