using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据转换实体（用于变量间的数据转发）
    /// </summary>
    [SugarTable("DataConversions")]
    public class DataConversion : EntityBase
    {
        /// <summary>
        /// 转换名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 源设备ID
        /// </summary>
        public int SourceDeviceId { get; set; }

        /// <summary>
        /// 源变量键
        /// </summary>
        public string SourceVariableKey { get; set; }

        /// <summary>
        /// 目标设备ID
        /// </summary>
        public int TargetDeviceId { get; set; }

        /// <summary>
        /// 目标变量键
        /// </summary>
        public string TargetVariableKey { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; }
    }
}