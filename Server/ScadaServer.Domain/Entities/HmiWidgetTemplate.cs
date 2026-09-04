using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// HMI 组件模板：组件库元数据的唯一真相源（替代前端硬编码 widgetRegistry）。
    /// 画布组件（HmiComponent.Type）存 RenderType；本表 TemplateKey 是注册键（两级匹配见前端设计）。
    /// </summary>
    [Table("HmiWidgetTemplates")]
    public class HmiWidgetTemplate
    {
        /// <summary>
        /// 主键ID，自增字段
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 模板唯一键（= 原注册键，如 title-header-tech-desktop）。唯一索引。
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string TemplateKey { get; set; } = string.Empty;

        /// <summary>
        /// 渲染类型（= 原 ComponentType，如 title-header）。
        /// builtin 轨 = SFC 映射键；svg 轨（D10）= 与 TemplateKey 同值。
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string RenderType { get; set; } = string.Empty;

        /// <summary>
        /// 模板名称
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 分类：equipment | sensors | structures | headers
        /// </summary>
        [Required]
        [MaxLength(32)]
        public string Category { get; set; } = "equipment";

        /// <summary>
        /// 描述
        /// </summary>
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 默认宽度（放置时的初始宽度）
        /// </summary>
        public int DefaultWidth { get; set; }

        /// <summary>
        /// 默认高度（放置时的初始高度）
        /// </summary>
        public int DefaultHeight { get; set; }

        /// <summary>
        /// 图标形态：lucide | div | svg | emoji
        /// </summary>
        [Required]
        [MaxLength(16)]
        public string IconKind { get; set; } = "lucide";

        /// <summary>
        /// 图标标识：lucide 图标名 / div 变体名（div-h/div-v/div-led）/ SVG 源码 / emoji 字符
        /// </summary>
        [MaxLength(2000)]
        public string IconKey { get; set; } = string.Empty;

        /// <summary>
        /// 图标颜色（Tailwind 类，如 text-amber-500；仅 lucide/emoji 生效）
        /// </summary>
        [MaxLength(64)]
        public string IconColor { get; set; } = string.Empty;

        /// <summary>
        /// 渲染轨：builtin（前端 SFC）| svg（通用 SVG 渲染器）
        /// </summary>
        [Required]
        [MaxLength(16)]
        public string RenderKind { get; set; } = "builtin";

        /// <summary>
        /// RenderKind=svg 时的 SVG 模板源码（已清洗）；其余强制 null
        /// </summary>
        [Column(TypeName = "text")]
        public string? SvgTemplate { get; set; }

        /// <summary>
        /// 默认属性 JSON（原 widgetRegistry.defaultProps() 求值序列化）
        /// </summary>
        [Column(TypeName = "text")]
        public string DefaultPropsJson { get; set; } = "{}";

        /// <summary>
        /// 属性表单 schema JSON（P5 启用）
        /// </summary>
        [Column(TypeName = "text")]
        public string PropSchemaJson { get; set; } = "[]";

        /// <summary>
        /// 系统内置模板（24 条种子）：可编辑/隐藏/排序，禁止删除
        /// </summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// 组件库显示顺序（升序，同序按 Id）
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// 创建时间（UTC）
        /// </summary>
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 更新时间（UTC）
        /// </summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
