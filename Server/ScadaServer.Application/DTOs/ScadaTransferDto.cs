using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ScadaServer.Application.DTOs
{
    /// <summary>组态迁移包格式标识</summary>
    public static class ScadaTransferFormats
    {
        public const string Project = "scada-project";
        public const string Page = "scada-page";
    }

    /// <summary>
    /// 组态迁移包统一序列化选项：camelCase（与 WebApi 线格式一致，前端可直接解析），
    /// 缩进输出便于人工检查。导入绑定使用 MVC 默认配置（大小写不敏感），PascalCase 文件亦可绑定。
    /// </summary>
    public static class ScadaTransferJson
    {
        public static JsonSerializerOptions Options { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    /// <summary>
    /// 图层配置 JSON 归一化共享逻辑（ScadaPageAppService / ScadaProjectAppService 复用）。
    /// 内容结构由前端负责，后端仅做轻量结构校验与透传。
    /// </summary>
    public static class ScadaLayerJson
    {
        /// <summary>
        /// 归一化图层配置 JSON：空白归 NULL；
        /// 结构校验：必须是 JSON 数组且每项含非空 id 字符串，否则归 NULL（前端回退默认单图层）。
        /// </summary>
        public static string? Normalize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var trimmed = json.Trim();
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object
                        || !item.TryGetProperty("id", out var idProp)
                        || idProp.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(idProp.GetString()))
                        return null;
                }
                return trimmed;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 组态迁移包根模型。工程级包（format=scada-project）携带 project + pages；
    /// 画面级包（format=scada-page）project 为空、pages 仅 1 项。
    /// 全部数据库自增 id 已剥离，导入时重新生成；变量绑定以设备业务键（Device.Key）携带。
    /// </summary>
    public class ScadaTransferPackageDto
    {
        /// <summary>迁移包格式标识；必填，取值见 <see cref="ScadaTransferFormats"/></summary>
        [Required]
        public string Format { get; set; } = string.Empty;

        /// <summary>迁移包格式版本，默认 1</summary>
        public int Version { get; set; } = 1;

        /// <summary>导出时间（UTC，可空）</summary>
        public DateTime? ExportedAt { get; set; }

        /// <summary>工程信息（工程级包有值；画面级包为空）</summary>
        public ScadaProjectTransferDto? Project { get; set; }

        /// <summary>页面列表（工程级包为全部页面；画面级包仅 1 项）</summary>
        public List<ScadaPageTransferDto> Pages { get; set; } = new();
    }

    /// <summary>
    /// 迁移包中的工程信息（不含自增 id，导入时重新生成）。
    /// </summary>
    public class ScadaProjectTransferDto
    {
        /// <summary>工程名称；必填（校验特性）</summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>工程描述</summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 迁移包中的页面信息（不含自增 id，导入时重新生成）。
    /// </summary>
    public class ScadaPageTransferDto
    {
        /// <summary>页面名称；必填（校验特性）</summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>是否为主页</summary>
        public bool IsHome { get; set; }

        /// <summary>Desktop / Mobile，导入时归一化</summary>
        public string Platform { get; set; } = "Desktop";

        /// <summary>画布宽度（像素），默认 1100</summary>
        public int Width { get; set; } = 1100;

        /// <summary>画布高度（像素），默认 700</summary>
        public int Height { get; set; } = 700;

        /// <summary>画布背景配置 JSON（可空）</summary>
        public string? BackgroundJson { get; set; }

        /// <summary>运行端自适应模式（可空）</summary>
        public string? AdaptMode { get; set; }

        /// <summary>页面图层配置 JSON（导入导出透传；旧版包缺失时为 null，兼容）</summary>
        public string? LayersJson { get; set; }

        /// <summary>该页面下的组件列表</summary>
        public List<ScadaComponentTransferDto> Components { get; set; } = new();
    }

    /// <summary>
    /// 迁移包中的页面组件信息（不含自增 id，导入时重新生成）。
    /// </summary>
    public class ScadaComponentTransferDto
    {
        /// <summary>组件类型；必填（校验特性）</summary>
        [Required]
        public string Type { get; set; } = string.Empty;

        /// <summary>组件名称；必填（校验特性）</summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>组件 X 坐标</summary>
        public int X { get; set; }

        /// <summary>组件 Y 坐标</summary>
        public int Y { get; set; }

        /// <summary>组件宽度</summary>
        public int Width { get; set; }

        /// <summary>组件高度</summary>
        public int Height { get; set; }

        /// <summary>组件层级（z-index）</summary>
        public int ZIndex { get; set; }

        /// <summary>组件归属图层 ID（uid 随包迁移，导入端不校验存在性，由前端兜底归层）</summary>
        public string? LayerId { get; set; }

        /// <summary>组件绑定字段（数据字段名）</summary>
        public string BindField { get; set; } = string.Empty;

        /// <summary>组件标签（可空）</summary>
        public string? Label { get; set; }

        /// <summary>导出源系统的设备业务键（Device.Key），导入时据此映射本系统设备 id</summary>
        public string? BindDeviceKey { get; set; }

        /// <summary>绑定变量键（可空）</summary>
        public string? BindVariableKey { get; set; }

        /// <summary>组件的扩展属性 JSON（默认 "{}"）</summary>
        public string PropsJson { get; set; } = "{}";
    }

    /// <summary>导入结果：新建实体 id/名称 + 计数 + 绑定失效等告警（前端逐条提示）</summary>
    public class ScadaImportResultDto
    {
        /// <summary>新建的工程ID</summary>
        public int ProjectId { get; set; }

        /// <summary>新建的工程名称</summary>
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>新建的页面ID（画面级包有值；工程级包为空）</summary>
        public int? PageId { get; set; }

        /// <summary>新建的页面名称（可空）</summary>
        public string? PageName { get; set; }

        /// <summary>成功导入的页面数</summary>
        public int ImportedPages { get; set; }

        /// <summary>成功导入的组件数</summary>
        public int ImportedComponents { get; set; }

        /// <summary>导入过程中的告警/绑定失效提示列表</summary>
        public List<string> Warnings { get; set; } = new();
    }
}
