using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// SCADA项目实体
    /// </summary>
    [Table("ScadaProjects")]
    public class ScadaProject : EntityBase
    {
        /// <summary>
        /// 项目名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 项目描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 项目包含的页面列表
        /// </summary>
        [NotMapped]
        public List<ScadaPage> Pages { get; set; } = new();
    }
}