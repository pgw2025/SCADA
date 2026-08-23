using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 暴露接口实体
    /// </summary>
    [Table("ExposedInterfaces")]
    public class ExposedInterface : EntityBase
    {
        /// <summary>
        /// 接口名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 路由URL
        /// </summary>
        public string RouteUrl { get; set; }

        /// <summary>
        /// 请求方法（GET/POST/PUT/DELETE等）
        /// </summary>
        public string RequestMethod { get; set; }

        /// <summary>
        /// 关联的设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 关联的设备
        /// </summary>
        public Device Device { get; set; }

        /// <summary>
        /// 暴露键（用于标识接口）
        /// </summary>
        public string ExposedKey { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; }
    }
}