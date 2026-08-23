using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SqlSugar;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 模型变量实体
    /// </summary>
    [Table("ModelVariables")]
    public class ModelVariable : EntityBase
    {
        /// <summary>
        /// 关联的数据模型ID
        /// </summary>
        public int ModelId { get; set; }

        /// <summary>
        /// 变量键（唯一标识）
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 信号类型（模拟量/数字量），由 DataType 推导：BIT/BOOL -> Digital，其余 -> Analog。
        /// 不再独立存储，避免与 DataType 矛盾。
        /// </summary>
        [SugarColumn(IsIgnore = true)]
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
        /// 设备地址（寄存器地址）
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// 变量描述
        /// </summary>
        
        public string? Description { get; set; }

        /// <summary>
        /// 是否存储历史数据（已由 StoreMode 替代：None 等价于不存储）。保留为只读派生，兼容旧调用。
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public bool IsStored => StoreMode != StoreModeEnum.None;

        /// <summary>
        /// 历史存储模式（None=不存储；Change/Cycle/Compressed/Aggregated）
        /// </summary>
        public StoreModeEnum StoreMode { get; set; } = StoreModeEnum.Change;

        /// <summary>
        /// 更新模式
        /// </summary>
        public UpdateMode UpdateMode { get; set; }

        /// <summary>
        /// 轮询间隔（毫秒），默认1000ms
        /// </summary>
        public int PollingIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 位偏移（用于位操作）
        /// </summary>
        
        public int? BitOffset { get; set; }

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
    }
}