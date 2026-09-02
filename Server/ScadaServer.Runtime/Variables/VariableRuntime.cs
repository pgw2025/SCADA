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

    /// <summary>位偏移。来源：DeviceVariable.BitOffset（设备实例级权威；模板层已移除该字段）。</summary>
    public int? BitOffset => Instance?.BitOffset;

    /// <summary>轮询间隔(ms)。来源：DeviceVariable.PollingIntervalMs，缺省回退 1000ms（模板层已移除该字段）。</summary>
    public int PollingIntervalMs => Instance?.PollingIntervalMs ?? 1000;

    /// <summary>
    /// 工程换算表达式（原始值 → 工程值，以 x 代表原始值）。
    /// 来源：DeviceVariable.ScaleExpressionOverride 优先，否则模板 ScaleExpression；空 = 恒等变换。
    /// 求值由 <c>ScadaServer.Runtime.DataConversion.VariableScaling</c> 在采集/写入链路调用。
    /// </summary>
    public string? ScaleExpression =>
        !string.IsNullOrWhiteSpace(Instance?.ScaleExpressionOverride)
            ? Instance!.ScaleExpressionOverride
            : Definition.ScaleExpression;

    /// <summary>死区。来源：DeviceVariable.DeadBandOverride 优先，否则模板 DeadBand。</summary>
    public double? DeadBand => Instance?.DeadBandOverride ?? Definition.DeadBand;

    /// <summary>该变量在设备实例上是否启用。</summary>
    public bool IsEnabled => Instance?.IsEnabled ?? true;

    /// <summary>有效读写权限。来源：DeviceVariable.IsReadOnlyOverride 优先，否则模板 IsReadOnly。</summary>
    public bool IsReadOnly => Instance?.IsReadOnlyOverride ?? Definition.IsReadOnly;

    /// <summary>历史存储模式（来自 ModelVariable 模板）。</summary>
    public StoreModeEnum StoreMode => Definition.StoreMode;

    /// <summary>历史存储周期（毫秒，来自 ModelVariable 模板；为将来 DeviceVariable 覆盖预留扩展口子）。</summary>
    public int StoreIntervalMs => Definition.StoreIntervalMs;

    /// <summary>下一次应执行轮询的时间点（由采集调度维护）。</summary>
    public DateTime NextPollTime { get; set; } = DateTime.MinValue;
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

    /// <summary>
    /// 绑定引擎最近一次写入本变量的期望值（运行时字段，不持久化）。
    /// 用于回声抑制：采集回读若等于该值且落在 <see cref="LastBindingWriteTime"/> 窗口内，
    /// 视为绑定写入的回显，不发布变化事件（历史/报警维持现状，由 IsChanged=false 自然生效）。
    /// </summary>
    public object? LastBindingWriteValue { get; set; }

    /// <summary>
    /// 绑定写入期望值的时间戳（运行时字段）。与 <see cref="LastBindingWriteValue"/> 配合判定回声窗口，超时后失效。
    /// </summary>
    public DateTime LastBindingWriteTime { get; set; }

    // ===================== 历史存储状态（运行时字段，不持久化） =====================

    /// <summary>
    /// 该变量最近一次"实际写入历史"的时间。运行时维护，用于判定"定时/兜底"是否到期。
    /// <para>
    /// 初始为 <see cref="DateTime.MinValue"/>：首次成功采集后立即写入一条"种子点"，
    /// 同时保证服务重启后每个启用变量各补一个点，避免趋势曲线在重启附近断档。
    /// 写入入口（DeviceWorker.TryRecordHistory）在每次成功落库后推进本字段。
    /// </para>
    /// </summary>
    public DateTime LastHistoryTime { get; set; } = DateTime.MinValue;

    /// <summary>
    /// 该变量最近一次写入历史的数值（运行时字段）。供 Change 模式的死区判定使用：
    /// 仅当 |当前值 - LastHistoryWrittenValue| 超过 <see cref="Definition.DeadBand"/> 时视为"有效变化"，
    /// 从而抑制微小抖动反复写库。非数值型变量不使用本字段（直接按 Equals 判定）。
    /// </summary>
    public double? LastHistoryWrittenValue { get; set; }
}
