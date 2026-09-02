using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 数据库版本实体（用于数据库迁移跟踪）
    /// </summary>
    [Table("DbVersion")]
    public class DbVersion
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 版本号
        /// </summary>
        public string Version { get; set; } = "";

        /// <summary>
        /// 应用时间（UTC 存储）
        /// </summary>
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    }
}