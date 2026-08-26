using ScadaServer.Domain.Enums;

namespace ScadaServer.Runtime.Events;

/// <summary>
/// 变量变化事件来源，用于区分触发方。绑定引擎后续可据此决定是否抑制回声或跳过历史/报警。
/// </summary>
public enum VariableChangeSource
{
    /// <summary>设备轮询采集引起的变化。</summary>
    Polling,

    /// <summary>前端/API 用户写入引起的变化。</summary>
    UserWrite,

    /// <summary>变量绑定引擎写入引起的变化。</summary>
    BindingWrite
}

/// <summary>
/// 进程内变量变化事件载荷。
/// 由 DeviceWorker（采集）与 RuntimeManager.WriteVariableAsync（用户写入）发布，
/// 供 VariableBindingEngine 等订阅者消费，无需经过 SignalR/MQTT 外发。
/// </summary>
public sealed class VariableChangeEvent
{
    /// <summary>设备 ID。</summary>
    public int DeviceId { get; init; }

    /// <summary>变量业务键。</summary>
    public string VariableKey { get; init; } = string.Empty;

    /// <summary>变化后的值。</summary>
    public object? Value { get; init; }

    /// <summary>变化前的值。</summary>
    public object? PreviousValue { get; init; }

    /// <summary>变量质量状态。</summary>
    public VariableQuality Quality { get; init; }

    /// <summary>更新时间戳。</summary>
    public DateTime UpdateTime { get; init; }

    /// <summary>变化来源。</summary>
    public VariableChangeSource Source { get; init; }
}
