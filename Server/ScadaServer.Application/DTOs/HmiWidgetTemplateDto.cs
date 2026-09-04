using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// HMI 组件模板 DTO（组件库元数据：模板键、渲染类型、图标、默认属性与 SVG 源码）。
    /// </summary>
    public class HmiWidgetTemplateDto
    {
        /// <summary>主键（创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>模板唯一键（= 原注册键，如 title-header-tech-desktop）；必填，唯一。</summary>
        [Required(ErrorMessage = "模板键不能为空")]
        [MaxLength(64, ErrorMessage = "模板键最长 64 字符")]
        public string TemplateKey { get; set; } = string.Empty;

        /// <summary>渲染类型（= 原 ComponentType，如 title-header）；必填。</summary>
        [Required(ErrorMessage = "渲染类型不能为空")]
        [MaxLength(64, ErrorMessage = "渲染类型最长 64 字符")]
        public string RenderType { get; set; } = string.Empty;

        /// <summary>模板名称；必填。</summary>
        [Required(ErrorMessage = "模板名称不能为空")]
        [MaxLength(100, ErrorMessage = "模板名称最长 100 字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>分类：equipment | sensors | structures | headers</summary>
        [Required(ErrorMessage = "分类不能为空")]
        [MaxLength(32)]
        public string Category { get; set; } = "equipment";

        /// <summary>描述</summary>
        [MaxLength(500, ErrorMessage = "描述最长 500 字符")]
        public string Description { get; set; } = string.Empty;

        /// <summary>默认宽度（放置时的初始宽度）</summary>
        [Range(1, 4096, ErrorMessage = "默认宽度超出合理范围")]
        public int DefaultWidth { get; set; }

        /// <summary>默认高度（放置时的初始高度）</summary>
        [Range(1, 4096, ErrorMessage = "默认高度超出合理范围")]
        public int DefaultHeight { get; set; }

        /// <summary>图标形态：lucide | div | svg | emoji</summary>
        [Required(ErrorMessage = "图标形态不能为空")]
        [MaxLength(16)]
        public string IconKind { get; set; } = "lucide";

        /// <summary>图标标识：lucide 图标名 / div 变体名 / SVG 源码 / emoji 字符</summary>
        [MaxLength(2000, ErrorMessage = "图标标识最长 2000 字符")]
        public string IconKey { get; set; } = string.Empty;

        /// <summary>图标颜色（Tailwind 类，如 text-amber-500；仅 lucide/emoji 生效）</summary>
        [MaxLength(64, ErrorMessage = "图标颜色最长 64 字符")]
        public string IconColor { get; set; } = string.Empty;

        /// <summary>渲染轨：builtin（前端 SFC）| svg（通用 SVG 渲染器）</summary>
        [Required(ErrorMessage = "渲染轨不能为空")]
        [MaxLength(16)]
        public string RenderKind { get; set; } = "builtin";

        /// <summary>RenderKind=svg 时的 SVG 模板源码（入库前经 SvgSanitizer 清洗）；builtin 轨强制 null。</summary>
        public string? SvgTemplate { get; set; }

        /// <summary>默认属性 JSON（原 widgetRegistry.defaultProps() 求值序列化）</summary>
        public string DefaultPropsJson { get; set; } = "{}";

        /// <summary>属性表单 schema JSON（P5 启用）</summary>
        public string PropSchemaJson { get; set; } = "[]";

        /// <summary>系统内置模板（24 条种子）：可编辑/隐藏/排序，禁止删除</summary>
        public bool IsSystem { get; set; }

        /// <summary>组件库显示顺序（升序，同序按 Id）</summary>
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// 模板导入载荷：单对象或 templates 数组由控制器适配（D11 兼容两种载荷）。
    /// </summary>
    public class WidgetTemplateImportDto
    {
        /// <summary>模板文件格式标识（须为 scada-widget-template）</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>文件版本</summary>
        public int Version { get; set; } = 1;

        /// <summary>待导入的模板</summary>
        public HmiWidgetTemplateDto Template { get; set; } = new();

        /// <summary>模板键冲突时的默认策略：overwrite（覆盖） | rename（追加随机后缀另存）</summary>
        public string ConflictMode { get; set; } = "rename";
    }

    /// <summary>
    /// 模板导出载荷（单条，附件下载）。
    /// </summary>
    public class WidgetTemplateExportDto
    {
        /// <summary>模板文件格式标识</summary>
        public string Format { get; set; } = "scada-widget-template";

        /// <summary>文件版本</summary>
        public int Version { get; set; } = 1;

        /// <summary>导出的模板</summary>
        public HmiWidgetTemplateDto Template { get; set; } = new();
    }

    /// <summary>
    /// 模板批量导出载荷（多模板打一个文件）。
    /// </summary>
    public class WidgetTemplateBundleDto
    {
        /// <summary>模板文件格式标识</summary>
        public string Format { get; set; } = "scada-widget-template";

        /// <summary>文件版本</summary>
        public int Version { get; set; } = 1;

        /// <summary>导出的模板集合</summary>
        public List<HmiWidgetTemplateDto> Templates { get; set; } = new();
    }

    /// <summary>
    /// 模板导入结果。
    /// </summary>
    public class ImportResult
    {
        /// <summary>是否成功</summary>
        public bool Ok { get; set; }

        /// <summary>落库后的模板主键</summary>
        public int Id { get; set; }

        /// <summary>导入模式：create | overwrite | renamed | skipped</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>rename 模式时返回实际新键</summary>
        public string? NewKey { get; set; }
    }
}
