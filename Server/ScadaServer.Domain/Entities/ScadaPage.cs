using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// SCADA页面实体
    /// </summary>
    [SugarTable("ScadaPages")]
    public class ScadaPage : EntityBase
    {
        /// <summary>
        /// 关联的项目ID
        /// </summary>
        public int ProjectId { get; set; }

        /// <summary>
        /// 关联的项目
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(ProjectId))]
        public ScadaProject Project { get; set; }

        /// <summary>
        /// 页面名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 是否为首页
        /// </summary>
        public bool IsHome { get; set; }

        /// <summary>
        /// 页面包含的HMI组件列表
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public List<HmiComponent> Components { get; set; }
    }
}