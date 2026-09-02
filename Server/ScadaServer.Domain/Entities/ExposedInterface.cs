using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 暴露接口实体
    /// </summary>
    [Table("ExposedInterfaces")]
    public class ExposedInterface
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 接口名称
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 路由URL
        /// </summary>
        [MaxLength(512)]
        public string RouteUrl { get; set; } = string.Empty;

        /// <summary>
        /// 请求方法（GET/POST/PUT/DELETE等）
        /// </summary>
        [MaxLength(16)]
        public string RequestMethod { get; set; } = string.Empty;

        /// <summary>
        /// 关联的设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 关联的设备
        /// </summary>
        public Device Device { get; set; } = null!;

        /// <summary>
        /// 暴露键（用于标识接口）
        /// </summary>
        [MaxLength(256)]
        public string ExposedKey { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; }
    }
}