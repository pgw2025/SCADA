using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ScadaServer.Application.Converters;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs;

/// <summary>
/// 数据模型变量模板 DTO（定义模型下每个变量的类型、存储、缩放与读写权限）。
/// </summary>
public class ModelVariableDto
{
    /// <summary>变量模板ID（主键，创建时由服务端生成）</summary>
    public int Id { get; set; }

    /// <summary>所属数据模型ID；必填，需大于 0（校验特性）</summary>
    [Range(1, int.MaxValue, ErrorMessage = "必须指定所属模型")]
    public int ModelId { get; set; }

    private string _key = string.Empty;
    private string _name = string.Empty;

    /// <summary>变量业务键（全局唯一）；必填，仅允许字母数字下划线，最长 50 字符。setter 自动 Trim（校验特性）</summary>
    [Required(ErrorMessage = "变量标识(Key)不能为空")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Key 只能包含字母、数字和下划线")]
    [StringLength(50, ErrorMessage = "Key 不能超过50个字符")]
    public string Key
    {
        get => _key;
        // 归一化须先于 [ApiController] 的模型校验执行：System.Text.Json 走 setter，
        // 这里 Trim 后校验拿到的才是规范化值，否则首尾空格永远过不了正则。
        set => _key = value?.Trim() ?? string.Empty;
    }

    /// <summary>变量名称；必填，最长 50 字符。setter 自动 Trim（校验特性）</summary>
    [Required(ErrorMessage = "变量名称不能为空")]
    [StringLength(50, ErrorMessage = "名称不能超过50个字符")]
    public string Name
    {
        get => _name;
        set => _name = value?.Trim() ?? string.Empty;
    }

    /// <summary>信号类型（由 DataType 派生，仅作输出）；以字符串枚举序列化</summary>
    // 信号类型由 DataType 派生(实体端 IsIgnore),此处仅作输出,不强制前端传入
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VariableType Type { get; set; }

    /// <summary>数据类型；必填，使用自定义枚举转换器（校验特性）</summary>
    [Required(ErrorMessage = "数据类型不能为空")]
    [JsonConverter(typeof(DataTypeEnumJsonConverter))]
    public DataTypeEnum DataType { get; set; }

    /// <summary>单位（可空）</summary>
    public string? Unit { get; set; }

    /// <summary>量程下限（可空）</summary>
    public double? Min { get; set; }

    /// <summary>量程上限（可空）</summary>
    public double? Max { get; set; }

    /// <summary>变量描述（可空）</summary>
    public string? Description { get; set; }

    /// <summary>是否启用历史存储</summary>
    public bool IsStored { get; set; }

    /// <summary>历史存储模式；以字符串枚举序列化，默认 Change（变化存储）</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StoreModeEnum StoreMode { get; set; } = StoreModeEnum.Change;

    /// <summary>
    /// 历史存储周期（毫秒）。Change 模式作为超时兜底周期，Cycle 模式作为定时采样周期。
    /// 下限 1000ms，默认 300000ms（5 分钟）。
    /// </summary>
    public int StoreIntervalMs { get; set; } = 300000;

    /// <summary>更新模式（变化/轮询等）；以字符串枚举序列化</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UpdateMode UpdateMode { get; set; }

    /// <summary>缩放斜率（工程缩放 y=slope*x+offset），默认 1.0</summary>
    public double ScaleSlope { get; set; } = 1.0;

    /// <summary>缩放偏移，默认 0.0</summary>
    public double ScaleOffset { get; set; } = 0.0;

    /// <summary>死区（变化阈值过滤，可空）</summary>
    public double? DeadBand { get; set; }

    /// <summary>是否只读（禁止外部写入），默认 true</summary>
    public bool IsReadOnly { get; set; } = true;

    /// <summary>扩展数据（可空，前端自定义附加字段）</summary>
    public Dictionary<string, string>? ExtensionData { get; set; }
}
