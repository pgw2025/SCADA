using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// SCADA页面实体
    /// </summary>
    [Table("ScadaPages")]
    public class ScadaPage : EntityBase
    {
        /// <summary>
        /// 关联的项目ID
        /// </summary>
        public int ProjectId { get; set; }

        /// <summary>
        /// 关联的项目
        /// </summary>
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
        /// 画面归属端：Desktop（桌面端）/ Mobile（移动端）。默认 Desktop。
        /// 同一工程下桌面端与移动端各自维护独立画面列表，互不关联。
        /// </summary>
        public string Platform { get; set; } = "Desktop";

        /// <summary>
        /// 画布宽度（像素），默认 1100
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 画布高度（像素），默认 700
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 页面包含的HMI组件列表
        /// </summary>
        [NotMapped]
        public List<HmiComponent> Components { get; set; }
    }
}