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
    /// 组态迁移包根模型。工程级包（format=scada-project）携带 project + pages；
    /// 画面级包（format=scada-page）project 为空、pages 仅 1 项。
    /// 全部数据库自增 id 已剥离，导入时重新生成；变量绑定以设备业务键（Device.Key）携带。
    /// </summary>
    public class ScadaTransferPackageDto
    {
        [Required]
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; } = 1;
        public DateTime? ExportedAt { get; set; }
        public ScadaProjectTransferDto? Project { get; set; }
        public List<ScadaPageTransferDto> Pages { get; set; } = new();
    }

    public class ScadaProjectTransferDto
    {
        [Required]
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class ScadaPageTransferDto
    {
        [Required]
        public string Name { get; set; } = string.Empty;
        public bool IsHome { get; set; }
        /// <summary>Desktop / Mobile，导入时归一化</summary>
        public string Platform { get; set; } = "Desktop";
        public int Width { get; set; } = 1100;
        public int Height { get; set; } = 700;
        public string? BackgroundJson { get; set; }
        public string? AdaptMode { get; set; }
        public List<ScadaComponentTransferDto> Components { get; set; } = new();
    }

    public class ScadaComponentTransferDto
    {
        [Required]
        public string Type { get; set; } = string.Empty;
        [Required]
        public string Name { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ZIndex { get; set; }
        public string BindField { get; set; } = string.Empty;
        public string? Label { get; set; }
        /// <summary>导出源系统的设备业务键（Device.Key），导入时据此映射本系统设备 id</summary>
        public string? BindDeviceKey { get; set; }
        public string? BindVariableKey { get; set; }
        public string PropsJson { get; set; } = "{}";
    }

    /// <summary>导入结果：新建实体 id/名称 + 计数 + 绑定失效等告警（前端逐条提示）</summary>
    public class ScadaImportResultDto
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int? PageId { get; set; }
        public string? PageName { get; set; }
        public int ImportedPages { get; set; }
        public int ImportedComponents { get; set; }
        public List<string> Warnings { get; set; } = new();
    }
}
