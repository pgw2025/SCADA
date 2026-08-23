using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 配置日志实体（记录设备配置变更）
    /// </summary>
    [SugarTable("ConfigLog")]
    public class ConfigLog : EntityBase
    {
        /// <summary>
        /// 关联的设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 操作人
        /// </summary>
        public string Operator { get; set; }

        /// <summary>
        /// 变更描述
        /// </summary>
        public string ChangeDesc { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; }
    }
}