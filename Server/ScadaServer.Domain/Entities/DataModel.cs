using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据模型实体
    /// </summary>
    [Table("DataModels")]
    public class DataModel : EntityBase
    {
        /// <summary>
        /// 模型名称
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 模型描述
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 厂商/型号描述（如 "Siemens S7-1500"、"罗克韦尔 1756"）。
        /// 仅作描述性信息，不决定协议——协议真相源统一为 <see cref="Type"/>。
        /// </summary>
        [MaxLength(100)]
        public string? VendorModel { get; set; }

        /// <summary>
        /// 协议类型（枚举）——数据模型的协议真相源。
        /// 一台设备通过 ModelId 绑定本模型，其驱动协议由本字段推导，
        /// 因此"未绑定任何设备的数据模型"也拥有明确协议，无需反查或回退。
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