using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// SCADA项目实体
    /// </summary>
    [SugarTable("ScadaProjects")]
    public class ScadaProject : EntityBase
    {
        /// <summary>
        /// 项目名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 项目描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 项目包含的页面列表
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public List<ScadaPage> Pages { get; set; }
    }
}