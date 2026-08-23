using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 设备表 - 物理设备的实例
    /// </summary>
    [Table("Devices")]
    public class Device : EntityBase
    {
        /// <summary>
        /// 设备名称
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 唯一键（用于运行时快速查找）。现由后台按区域自动生成，全局唯一。
        /// </summary>
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// 所属区域ID
        /// </summary>
        public int AreaId { get; set; }

        /// <summary>
        /// 关联区域
        /// </summary>
        public Area? Area { get; set; }

        /// <summary>
        /// 关联变量模型ID
        /// </summary>
        public int ModelId { get; set; }

        /// <summary>
        /// 关联变量模型
        /// </summary>
        public DataModel? Model { get; set; }

        /// <summary>
        /// 设备类型（枚举）
        /// </summary>
        public DeviceType Type { get; set; }

        /// <summary>
        /// 是否启用采集
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 采集周期（毫秒）
        /// 高速PLC: 100ms, 普通PLC: 500ms, 仪表: 5000ms
        /// </summary>
        public int PollingInterval { get; set; } = 1000;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 配置更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后一次通信时间（仅记录，不用于运行时状态）
        /// </summary>
        
        public DateTime? LastCommunicationTime { get; set; }

        /// <summary>
        /// 协议配置（一对一）
        /// </summary>
        public DeviceConfig? Config { get; set; }

        /// <summary>
        /// 该设备下的触发器
        /// </summary>
        public List<VariableTrigger>? Triggers { get; set; }
    }
}