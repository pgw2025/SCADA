using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// SCADA页面实体
    /// </summary>
    [Table("ScadaPages")]
    public class ScadaPage
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        /// <summary>
        /// 关联的项目ID
        /// </summary>
        public int ProjectId { get; set; }

        /// <summary>
        /// 关联的项目
        /// </summary>
        public ScadaProject Project { get; set; } = null!;

        /// <summary>
        /// 页面名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

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
        /// 画布背景配置（JSON，前端序列化）：类型（纯色/渐变/图片）及对应参数。
        /// NULL 表示未配置，前端回退默认白底。
        /// </summary>
        public string? BackgroundJson { get; set; }

        /// <summary>
        /// 运行端自适应屏幕模式：FitScaleUp（等比缩放-允许放大）/ Stretch（拉伸填满）。
        /// NULL/空 表示未配置，前端回退兼容行为（等比缩小不放大）。
        /// </summary>
        public string? AdaptMode { get; set; }

        /// <summary>
        /// 页面图层配置（JSON 数组，前端序列化）：[{id,name,visible,locked,opacity,colorBadge},...]。
        /// NULL 表示未配置，前端回退默认单图层。
        /// </summary>
        public string? LayersJson { get; set; }

        /// <summary>
        /// 页面包含的HMI组件列表
        /// </summary>
        [NotMapped]
        public List<HmiComponent> Components { get; set; } = new();
    }
}