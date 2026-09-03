using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 设备-数据模型绑定实体（阶段 5 引入）：设备与数据模型的多对多绑定中间表。
    /// <para>
    /// 设计目标：<see cref="Device.ModelId"/> 作为"主模型"快捷列与既有代码的兼容锚点被反范式保留；
    /// 本表维护设备的全部模型绑定，其中 <c>IsPrimary=true</c> 的行必须与 <c>Device.ModelId</c> 一致
    /// （双向同步单点收敛于 DeviceAppService / DeviceDataModelAppService，见方案文档 07-阶段5）。
    /// </para>
    /// <para>
    /// 保守策略：运行时变量解析仍以"主模型"为唯一生效集合——运行时 Include 链走
    /// <see cref="Device.Model"/>（主模型）保持不变（零运行时改动）；附加（非主）模型绑定仅供管理界面
    /// 与未来多模型变量合并使用，多模型合并（跨模型同名 Key 冲突消解）不在本阶段范围。
    /// </para>
    /// </summary>
    [Table("DeviceDataModels")]
    public class DeviceDataModel
    {
        /// <summary>主键 ID，自增字段。</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>设备 ID（FK → Devices，Cascade：删设备自动清绑定行）。</summary>
        public int DeviceId { get; set; }

        /// <summary>设备导航属性（对应 <see cref="DeviceId"/>）。</summary>
        [ForeignKey(nameof(DeviceId))]
        public Device? Device { get; set; }

        /// <summary>数据模型 ID（FK → DataModels，Restrict：被绑定模型不可删除，杜绝静默级联）。</summary>
        public int DataModelId { get; set; }

        /// <summary>数据模型导航属性（对应 <see cref="DataModelId"/>）。</summary>
        [ForeignKey(nameof(DataModelId))]
        public DataModel? DataModel { get; set; }

        /// <summary>
        /// 绑定版本快照（取绑定时刻模型的 <see cref="DataModel.Version"/>，记录"当时用的版本"）。
        /// 上限 20 字符，与 DataModel.Version 一致。
        /// </summary>
        [MaxLength(20)]
        public string Version { get; set; } = "1.0";

        /// <summary>是否主模型（一台设备至多一条 IsPrimary=true，应用层校验保障；默认 false）。</summary>
        public bool IsPrimary { get; set; }

        /// <summary>绑定是否启用（默认 true；MVP 阶段预留，运行时暂不按此过滤）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC 存储）。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC 存储）。</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
