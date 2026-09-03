using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 变量历史数据实体（时序点）。
    /// <para>
    /// 由运行时采集循环按变量存储策略（StoreMode：Change=变化存储 / Cycle=周期存储）写入。
    /// 查询端通过 <c>VariableKey</c> + 时间倒序取最近 N 条，供历史趋势曲线展示。
    /// </para>
    /// <para>
    /// 使用独立主键（long 自增）而非 <see cref="EntityBase"/>（int Id），
    /// 因为历史数据量远大于业务数据，long 可支撑更长时间维度。
    /// </para>
    /// </summary>
    [Table("VariableHistory")]
    public class VariableHistory
    {
        /// <summary>
        /// 主键（自增）
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// 所属设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 所属设备标识（冗余存储，便于按设备维度查询）
        /// </summary>
        public string DeviceKey { get; set; } = string.Empty;

        /// <summary>
        /// 变量业务键（对应 DataPoint.Key / DataPointMapping.Key）
        /// </summary>
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 变量名称（冗余存储，避免查询时再关联变量表）
        /// </summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>
        /// 数值化后的值。非数值型（如 STRING / 布尔）时：数字量存 0/1，其余存 0，原始值见 <see cref="RawValue"/>。
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// 原始值字符串（保留驱动返回的原始形态，避免数值化丢失信息）。
        /// </summary>
        public string? RawValue { get; set; }

        /// <summary>
        /// 采样时间（设备采集时间，非落库时间）
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 采样质量（如 Good / CommunicationError 等，冗余自 VariableQuality 枚举名）
        /// </summary>
        public string? Quality { get; set; }
    }
}
