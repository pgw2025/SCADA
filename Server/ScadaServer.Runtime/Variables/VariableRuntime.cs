using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;

/// <summary>
/// 变量运行时对象（即"RuntimeVariable"）。
/// 聚合"变量模板定义"(<see cref="ModelVariable"/>) 与"设备实例配置"(<see cref="DeviceVariable"/>)，
/// 形成运行时实际使用的解析结果，供采集调度与驱动调用消费。
/// <para>
/// 设计要点：
/// 1. 变量"是什么"（Key / Name / DataType / Unit / 缩放定义等）来自 <see cref="Definition"/>（ModelVariable 模板）；
/// 2. 变量"在具体设备上怎么实现"（Address / BitOffset / PollingInterval / 缩放覆盖等）来自 <see cref="Instance"/>（DeviceVariable 设备配置）；
/// 3. 解析后的实际地址 / 轮询间隔 / 缩放均优先取自设备实例，为空时回退到模板；
/// 4. <b>运行时严禁直接访问 ModelVariable.Address</b>——地址一律经 <see cref="Address"/> 由 DeviceVariable 提供。
/// </para>
/// </summary>
public class VariableRuntime : IRuntimeVariable
{
    /// <summary>变量模板定义（来自 ModelVariable）。</summary>
    public ModelVariable Definition { get; init; } = null!;

    /// <summary>设备实例配置（来自 DeviceVariable）。过渡期理论上一经初始化即有值；为空时按模板兜底。</summary>
    public DeviceVariable? Instance { get; init; }

    // ===================== 变量定义（来自 ModelVariable） =====================
    /// <summary>变量业务键（来自 ModelVariable.Key）。</summary>
    public string Key => Definition.Key;

    /// <summary>变量名称（来自 ModelVariable.Name）。</summary>
    public string Name => Definition.Name;

    /// <summary>数据类型（来自 ModelVariable.DataType）。</summary>
    public DataTypeEnum DataType => Definition.DataType;

    /// <summary>单位（来自 ModelVariable.Unit）。</summary>
    public string? Unit => Definition.Unit;

    /// <summary>最小值（来自 ModelVariable.Min）。</summary>
    public double? Min => Definition.Min;

    /// <summary>最大值（来自 ModelVariable.Max）。</summary>
    public double? Max => Definition.Max;

    // ===================== 设备配置（来自 DeviceVariable，回退模板） =====================

    /// <summary>
    /// 实际寄存器地址。来源：DeviceVariable.Address（权威）。
    /// <para>本阶段不回退到 ModelVariable.Address（已禁止运行时访问），缺失视为空地址。</para>
    /// </summary>
    public string Address => Instance?.Address ?? string.Empty;

    /// <summary>位偏移。来源：DeviceVariable.BitOffset 优先，否则回退模板 BitOffset（已 [Obsolete]）。</summary>
    public int? BitOffset
    {
        get
        {
            if (Instance?.BitOffset is { } v) return v;
            #pragma warning disable CS0618
            return Definition.BitOffset;
            #pragma warning restore CS0618
        }
    }

    /// <summary>轮询间隔(ms)。来源：DeviceVariable.PollingIntervalMs 优先，否则回退模板 PollingIntervalMs（已 [Obsolete]）。</summary>
    public int PollingIntervalMs
    {
        get
        {
            if (Instance?.PollingIntervalMs is { } v) return v;
            #pragma warning disable CS0618
            return Definition.PollingIntervalMs;
            #pragma warning restore CS0618
        }
    }

    /// <summary>缩放斜率(Scale)。来源：DeviceVariable.ScaleSlopeOverride 优先，否则模板 ScaleSlope。</summary>
    public double ScaleSlope => Instance?.ScaleSlopeOverride ?? Definition.ScaleSlope;

    /// <summary>缩放偏移(Scale)。来源：DeviceVariable.ScaleOffsetOverride 优先，否则模板 ScaleOffset。</summary>
    public double ScaleOffset => Instance?.ScaleOffsetOverride ?? Definition.ScaleOffset;

    /// <summary>死区。来源：DeviceVariable.DeadBandOverride 优先，否则模板 DeadBand。</summary>
    public double? DeadBand => Instance?.DeadBandOverride ?? Definition.DeadBand;

    /// <summary>该变量在设备实例上是否启用。</summary>
    public bool IsEnabled => Instance?.IsEnabled ?? true;

    /// <summary>下一次应执行轮询的时间点（由采集调度维护）。</summary>
    public DateTime NextPollTime { get; set; } = DateTime.MinValue;

    // ===================== 运行时状态（采集结果） =====================
    /// <summary>当前值。</summary>
    public object? Value { get; set; }

    /// <summary>上一个值（用于检测变化）。</summary>
    public object? PreviousValue { get; set; }

    /// <summary>最后更新时间戳。</summary>
    public DateTime UpdateTime { get; set; }

    /// <summary>变量质量状态（Good/Bad/Uncertain）。</summary>
    public VariableQuality Quality { get; set; }

    /// <summary>值是否发生变化。</summary>
    public bool IsChanged { get; set; }
}
