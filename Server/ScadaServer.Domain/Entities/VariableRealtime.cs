using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 变量实时快照实体（MySQL 实时库）。
    /// <para>
    /// 以 (DeviceId, VariableKey) 为复合主键，每设备每变量仅一行，保存最新一次采集快照
    /// （值/原始值/质量/采样时间），由实时快照服务周期性批量 Upsert。
    /// 使实时值具备持久化能力，服务重启后仍可恢复展示，并与“实时库使用 MySQL”的目标对齐。
    /// </para>
    /// </summary>
    [Table("VariableRealtime")]
    public class VariableRealtime
    {
        /// <summary>
        /// 所属设备ID（复合主键之一）
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 所属设备标识（冗余存储，便于按设备维度查询）
        /// </summary>
        public string DeviceKey { get; set; } = string.Empty;

        /// <summary>
        /// 变量业务键（对应 DataPoint.Key / DataPointMapping.Key，复合主键之一）
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
