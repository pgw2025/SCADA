using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据转换实体（用于变量间的数据转发）
    /// </summary>
    [Table("DataConversions")]
    public class DataConversion
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 转换名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 源设备ID
        /// </summary>
        public int SourceDeviceId { get; set; }

        /// <summary>
        /// 源变量键
        /// </summary>
        public string SourceVariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 目标设备ID
        /// </summary>
        public int TargetDeviceId { get; set; }

        /// <summary>
        /// 目标变量键
        /// </summary>
        public string TargetVariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// 越限值策略（根因 A4）：Clamp=夹取到 [Min,Max] 后写入 / Reject=越限拒绝。
        /// 默认 Clamp（消除"值一超限就不转发"的偶发丢失）。
        /// </summary>
        public string OutOfRangePolicy { get; set; } = "Clamp";
    }
}