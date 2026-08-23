using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 区域实体（用于设备分组管理）
    /// </summary>
    [SugarTable("Areas")]
    public class Area : EntityBase
    {
        /// <summary>
        /// 区域名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 区域描述
        /// </summary>
        public string Description { get; set; }
    }
}