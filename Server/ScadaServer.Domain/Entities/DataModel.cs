using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据模型实体，描述一类设备"是什么型号"，与"如何通信"解耦。
    /// <para>
    /// 协议由设备所附 <see cref="DeviceConnection"/> 决定（运行期 / 驱动统一按 <c>Protocol.Key</c> 派发驱动），
    /// 本实体不再绑定协议，与驱动方式彻底解耦。
    /// </para>
    /// </summary>
    [Table("DataModels")]
    public class DataModel
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 模型名称（业务可读的名称，如 "1500 主控"）。
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 模型编码（业务唯一键，阶段 4 新增）。
        /// <para>可空：存量数据回填取 <see cref="Name"/>（重名追加 -2/-3 去重后缀）；库级唯一索引
        /// <c>ix_datamodels_code</c> 建于回填之后（独立迁移），NULL 允许多条共存。</para>
        /// </summary>
        [MaxLength(100)]
        public string? Code { get; set; }

        /// <summary>
        /// 模型版本号，默认 "1.0"
        /// </summary>
        [MaxLength(20)]
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// 是否已发布，默认 true。用于标识模型是否允许被新建设备引用/下发运行。
        /// </summary>
        public bool IsPublished { get; set; } = true;

        /// <summary>
        /// 设备厂商（如 "Siemens"、"Schneider"）。
        /// </summary>
        [MaxLength(100)]
        public string? Vendor { get; set; }

        /// <summary>
        /// 厂商/型号描述（如 "Siemens S7-1500"、"Schneider M340"）。
        /// 仅作描述性信息，可由 <see cref="Vendor"/> 与模型名称拼接得出，也可手动填写。
        /// </summary>
        [MaxLength(100)]
        public string? VendorModel { get; set; }

        /// <summary>
        /// 模型描述
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 创建时间（UTC 存储）
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 更新时间（UTC 存储）
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 模型包含的变量列表
        /// </summary>
        [NotMapped]
        public List<DataPoint>? Variables { get; set; }
    }
}
