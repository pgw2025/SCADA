using SqlSugar;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 模型变量实体
    /// </summary>
    [SugarTable("ModelVariables")]
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
        /// 变量类型（输入/输出/内存等）
        /// </summary>
        public VariableType Type { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public DataTypeEnum DataType { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? Unit { get; set; }

        /// <summary>
        /// 最小值
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public double? Min { get; set; }

        /// <summary>
        /// 最大值
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public double? Max { get; set; }

        /// <summary>
        /// 设备地址（寄存器地址）
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// 变量描述
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? Description { get; set; }

        /// <summary>
        /// 是否存储
        /// </summary>
        public bool IsStored { get; set; }

        /// <summary>
        /// 存储模式
        /// </summary>
        public string StoreMode { get; set; }

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
        [SugarColumn(IsNullable = true)]
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
        [SugarColumn(IsNullable = true)]
        public double? DeadBand { get; set; }

        /// <summary>
        /// 是否只读，默认true
        /// </summary>
        public bool IsReadOnly { get; set; } = true;

        /// <summary>
        /// 扩展数据（JSON格式）
        /// </summary>
        [SugarColumn(ColumnDataType = "longtext", IsJson = true)]
        public Dictionary<string, string>? ExtensionData { get; set; }
    }
}