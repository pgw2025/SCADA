using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 设备表 - 物理设备的实例
    /// </summary>
    [Table("Devices")]
    public class Device
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
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
        /// 所属控制器 ID（可空：阶段 3 过渡列，未回填/手工场景可为 NULL；FK → Controllers，Restrict）。
        /// </summary>
        public int? ControllerId { get; set; }

        /// <summary>
        /// 所属控制器导航属性（对应 <see cref="ControllerId"/>）。
        /// </summary>
        [ForeignKey(nameof(ControllerId))]
        public Controller? Controller { get; set; }

        /// <summary>
        /// 默认设备连接 ID（可空：历史过渡列，未回填/手工场景可为 NULL；FK → DeviceConnections，Restrict）。
        /// <para>
        /// 阶段 6 起设备连接参数的唯一来源：非空时以 <see cref="DeviceConnection.ConfigJson"/> 为连接配置
        /// （原 Device.JsonConfig 历史列已于阶段 6.4 删除）。
        /// </para>
        /// </summary>
        public int? ConnectionId { get; set; }

        /// <summary>
        /// 默认设备连接导航属性（对应 <see cref="ConnectionId"/>）。
        /// </summary>
        [ForeignKey(nameof(ConnectionId))]
        public DeviceConnection? Connection { get; set; }

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
        /// 创建时间（UTC 存储）
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 配置更新时间（UTC 存储）
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

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
        /// 该设备下的设备变量实例（变量在设备上的具体实现）。
        /// <para>
        /// 一台设备可包含多条 <see cref="DataPointMapping"/>，每条对应其模型 <see cref="DataModel"/> 中
        /// 一个 <see cref="DataPoint"/> 的实例化；设备实例上可覆盖地址、轮询间隔、缩放、死区等。
        /// </para>
        /// </summary>
        public List<DataPointMapping> DataPointMappings { get; set; } = new();

        /// <summary>
        /// 设备-数据模型绑定（阶段 5：多对多中间表，删设备时随 FK Cascade 自动清理）。
        /// <para>
        /// 主模型（IsPrimary=true）行与 <see cref="ModelId"/> 保持严格一致（双写单点维护）；
        /// 附加（非主）模型绑定仅供管理界面与未来扩展，运行时仍只认主模型（<see cref="Model"/>）。
        /// </para>
        /// </summary>
        public List<DeviceDataModel> DeviceDataModels { get; set; } = new();
    }
}