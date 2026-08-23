using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ScadaServer.Application.Converters;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs;

public class ModelVariableDto
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "必须指定所属模型")]
    public int ModelId { get; set; }

    [Required(ErrorMessage = "变量标识(Key)不能为空")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Key 只能包含字母、数字和下划线")]
    [StringLength(50, ErrorMessage = "Key 不能超过50个字符")]
    public string Key { get; set; } = string.Empty;

    [Required(ErrorMessage = "变量名称不能为空")]
    [StringLength(50, ErrorMessage = "名称不能超过50个字符")]
    public string Name { get; set; } = string.Empty;

    // 信号类型由 DataType 派生(实体端 IsIgnore),此处仅作输出,不强制前端传入
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VariableType Type { get; set; }

    [Required(ErrorMessage = "数据类型不能为空")]
    [JsonConverter(typeof(DataTypeEnumJsonConverter))]
    public DataTypeEnum DataType { get; set; }

    public string? Unit { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }

    // 地址非空校验下放到 AppService.ValidateVariableLogic,按协议区分:
    // 虚拟设备无需物理地址,允许为空;其余协议仍要求非空。
    public string Address { get; set; } = string.Empty;

    public string? Description { get; set; }
    public bool IsStored { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StoreModeEnum StoreMode { get; set; } = StoreModeEnum.Change;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UpdateMode UpdateMode { get; set; }

    [Range(1, 3600000, ErrorMessage = "采集频率必须在 1ms 到 1小时之间")]
    public int PollingIntervalMs { get; set; } = 1000;

    // --- 工业级增强字段 ---
    
    public int? BitOffset { get; set; }
    public double ScaleSlope { get; set; } = 1.0;
    public double ScaleOffset { get; set; } = 0.0;
    public double? DeadBand { get; set; }
    public bool IsReadOnly { get; set; } = true;

    public Dictionary<string, string>? ExtensionData { get; set; }
}
