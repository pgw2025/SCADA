using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ScadaServer.Application.Converters;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs;

/// <summary>
/// 设备变量 DTO：聚合"变量模板定义（来自 ModelVariable）"与"设备实例配置（来自 DeviceVariable）"。
/// 前端通过 Device.Variables 获取每台设备上每个变量的定义信息与在设备上的落地配置。
/// </summary>
public class DeviceVariableDto
{
    /// <summary>设备变量实例ID（DeviceVariable.Id）</summary>
    public int Id { get; set; }

    /// <summary>所属设备ID</summary>
    public int DeviceId { get; set; }

    /// <summary>关联变量模板ID（ModelVariable.Id）</summary>
    public int ModelVariableId { get; set; }

    // ===== 变量定义（来自 ModelVariable 模板）=====

    /// <summary>变量标识（来自模板，全局唯一键）</summary>
    [StringLength(100, ErrorMessage = "变量键不能超过100个字符")]
    public string Key { get; set; } = string.Empty;

    /// <summary>变量名称（来自模板）</summary>
    [StringLength(100, ErrorMessage = "变量名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;

    /// <summary>数据类型（来自模板）</summary>
    [JsonConverter(typeof(DataTypeEnumJsonConverter))]
    public DataTypeEnum DataType { get; set; }

    /// <summary>单位（来自模板）</summary>
    [StringLength(50, ErrorMessage = "单位不能超过50个字符")]
    public string? Unit { get; set; }

    // ===== 设备实例配置（来自 DeviceVariable）=====

    /// <summary>设备实例上的实际寄存器地址。空 → 回退模板（过渡期兼容）。</summary>
    [StringLength(500, ErrorMessage = "地址不能超过500个字符")]
    public string? Address { get; set; }

    /// <summary>设备实例结构化地址（JSON，<c>AddressConfig</c>）。地址权威机读形态，前端仅编辑本字段。</summary>
    [StringLength(3000, ErrorMessage = "地址配置不能超过3000个字符")]
    public string? AddressConfigJson { get; set; }

    /// <summary>位偏移（用于位操作）。空 → 回退模板。</summary>
    [Range(0, 31, ErrorMessage = "位偏移必须在0到31之间")]
    public int? BitOffset { get; set; }

    /// <summary>设备实例上的轮询间隔（毫秒）。空 → 回退模板（或设备级 PollingInterval）。</summary>
    [Range(10, 3600000, ErrorMessage = "轮询间隔必须在10ms到1小时之间")]
    public int? PollingIntervalMs { get; set; }

    /// <summary>是否在该设备上启用此变量的采集。默认 true。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>工程换算表达式覆盖值（以 x 代表原始值）。空 → 使用模板 ScaleExpression。</summary>
    [StringLength(200, ErrorMessage = "换算表达式不能超过200个字符")]
    public string? ScaleExpressionOverride { get; set; }

    /// <summary>死区覆盖值。空 → 使用模板 DeadBand。</summary>
    public double? DeadBandOverride { get; set; }

    /// <summary>读写权限覆盖值。空 → 继承模板 IsReadOnly。</summary>
    public bool? IsReadOnlyOverride { get; set; }

    // ===== 回显字段（来自 ModelVariable 模板，只出不进）=====

    /// <summary>模板定义的只读权限（用于前端展示"继承"时的模板当前值）。</summary>
    public bool TemplateIsReadOnly { get; set; }

    /// <summary>有效权限 = IsReadOnlyOverride ?? TemplateIsReadOnly（运行时实际生效值）。</summary>
    public bool EffectiveIsReadOnly { get; set; }
}

/// <summary>
/// 变量写入请求 DTO：设备运行时写入变量的原始值（驱动按变量 DataType 转换为物理类型）。
/// </summary>
public class WriteVariableRequestDto
{
    /// <summary>待写入的原始值（数字或布尔）。</summary>
    public object? Value { get; set; }
}
