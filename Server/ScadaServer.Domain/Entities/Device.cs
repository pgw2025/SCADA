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
        /// 最近一次已知的运行时状态（持久化）。
        /// 由运行时在状态变更时回写，用于进程重启后仍有最后状态可读，
        /// 避免重启瞬间所有设备显示为未定义的默认状态。
        /// 实时状态仍以运行时内存态（RuntimeStatus）为准。
        /// </summary>
        public DeviceStatus? LastKnownStatus { get; set; }

        /// <summary>
        /// 协议配置（一对一）
        /// </summary>
        public DeviceConfig? Config { get; set; }

        /// <summary>
        /// 该设备下的触发器
        /// </summary>
        public List<VariableTrigger>? Triggers { get; set; }

        /// <summary>
        /// 该设备下的设备变量实例（变量在设备上的具体实现）。
        /// <para>
        /// 一台设备可包含多条 <see cref="DeviceVariable"/>，每条对应其模型 <see cref="DataModel"/> 中
        /// 一个 <see cref="ModelVariable"/> 的实例化；设备实例上可覆盖地址、轮询间隔、缩放、死区等。
        /// </para>
        /// </summary>
        public List<DeviceVariable>? DeviceVariables { get; set; }
    }
}