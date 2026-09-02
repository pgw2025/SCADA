using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 模型变量实体
    /// </summary>
    [Table("ModelVariables")]
    public class ModelVariable
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 关联的数据模型ID
        /// </summary>
        public int ModelId { get; set; }

        /// <summary>
        /// 变量键（模型内唯一标识）
        /// </summary>
        [MaxLength(50)]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// 变量名称
        /// </summary>
        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 信号类型（模拟量/数字量），由 DataType 推导：BIT/BOOL -> Digital，其余 -> Analog。
        /// 不再独立存储，避免与 DataType 矛盾。
        /// </summary>
        [NotMapped]
        public VariableType Type =>
            (DataType == DataTypeEnum.BIT || DataType == DataTypeEnum.BOOL)
                ? VariableType.Digital
                : VariableType.Analog;

        /// <summary>
        /// 数据类型
        /// </summary>
        public DataTypeEnum DataType { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        [MaxLength(32)]
        public string? Unit { get; set; }

        /// <summary>
        /// 最小值
        /// </summary>
        
        public double? Min { get; set; }

        /// <summary>
        /// 最大值
        /// </summary>
        
        public double? Max { get; set; }

        /// <summary>
        /// 变量描述
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 是否存储历史数据（已由 StoreMode 替代：None 等价于不存储）。保留为只读派生，兼容旧调用。
        /// </summary>
        [NotMapped]
        public bool IsStored => StoreMode != StoreModeEnum.None;

        /// <summary>
        /// 历史存储模式（None=不存储；Change/Cycle/Compressed/Aggregated）
        /// </summary>
        public StoreModeEnum StoreMode { get; set; } = StoreModeEnum.Change;

        /// <summary>
        /// 历史存储周期（毫秒）。与 <see cref="StoreMode"/> 配合决定"何时写入"一条历史采样点：
        /// <para>
        /// - Change（变化存储）：作为"超时兜底"周期——值变化即写入（含死区去抖），
        ///   若值长时间未变化，超过本周期也强制写入一条，避免趋势曲线断档；
        /// - Cycle/Compressed/Aggregated（周期类存储）：作为定时采样周期，按本间隔周期写入原始点，与轮询间隔解耦；
        /// - None：不启用历史存储，本字段无意义。
        /// </para>
        /// <para>默认 300000ms（5 分钟）。下限 1000ms，由应用层与前端共同校验。</para>
        /// </summary>
        public int StoreIntervalMs { get; set; } = 300000;

        /// <summary>
        /// 更新模式
        /// </summary>
        public UpdateMode UpdateMode { get; set; }

        /// <summary>
        /// 缩放斜率（系数），默认1.0
        /// </summary>
        public double ScaleSlope { get; set; } = 1.0;

        /// <summary>
        /// 缩放偏移量，默认0.0
        /// </summary>
        public double ScaleOffset { get; set; } = 0.0;

        /// <summary>
        /// 死区值（用于变化检测）
        /// </summary>
        
        public double? DeadBand { get; set; }

        /// <summary>
        /// 是否只读，默认true
        /// </summary>
        public bool IsReadOnly { get; set; } = true;

        /// <summary>
        /// 扩展数据（JSON格式）
        /// </summary>
        [Column(TypeName = "longtext")]
        public Dictionary<string, string>? ExtensionData { get; set; }

        /// <summary>
        /// 该模型变量在各设备上的实例化集合（<see cref="DeviceVariable"/>）。
        /// <para>
        /// 一个 <see cref="ModelVariable"/> 模板可被多台设备各自实例化出一条 <see cref="DeviceVariable"/>，
        /// 因此这里是 1:N 关系；具体地址、轮询、缩放等实现细节由设备实例决定。
        /// </para>
        /// </summary>
        public List<DeviceVariable> DeviceVariables { get; set; } = new();
    }
}