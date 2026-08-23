using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 设备配置实体（存储设备的协议配置）
    /// </summary>
    [Table("DeviceConfigs")]
    public class DeviceConfig
    {
        /// <summary>
        /// 关联的设备ID（主键）
        /// </summary>
        [Key]
        public int DeviceId { get; set; }

        /// <summary>
        /// JSON格式的配置内容
        /// </summary>
        [Column(TypeName = "longtext")]
        public string JsonConfig { get; set; } = string.Empty;

        /// <summary>
        /// 配置版本号
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}