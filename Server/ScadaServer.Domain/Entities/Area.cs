using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 区域实体（用于设备分组管理）
    /// </summary>
    [Table("Areas")]
    public class Area : EntityBase
    {
        /// <summary>
        /// 区域名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 区域编码（稳定短码，如 BLR）。用于设备编号自动生成的前缀；留空时回退为 A{Id}
        /// </summary>
        
        public string? Code { get; set; }

        /// <summary>
        /// 区域描述
        /// </summary>
        public string Description { get; set; }
    }
}