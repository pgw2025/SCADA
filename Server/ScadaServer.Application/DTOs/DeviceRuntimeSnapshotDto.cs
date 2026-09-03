using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs;

/// <summary>
/// 设备运行时聚合快照：一次返回连接态 + 运行态 + 报警 + 通信统计。
/// 供前端进入设备详情页时初始化首帧；实时变化仍走 SignalR（变量值）与快照轮询（RunState/统计）。
/// <para>
/// 线程安全说明：字段间一致性不保证。快照由 RuntimeManager 无锁读取多个内存字段，
/// .NET 保证单字段读写原子性，但字段间可能不一致（例如读到 LastError 新值 + ConsecutiveFailureCount
/// 旧值），误差窗口 ≤ 一个采集轮次。对可观测快照这是可接受且刻意为之——对 DeviceRuntime.Lock
/// 加锁读取会与采集循环互相阻塞，明确禁止（方案 P6）。
/// </para>
/// </summary>
public class DeviceRuntimeSnapshotDto
{
    // ---- 身份 ----
    public int DeviceId { get; init; }
    public string DeviceKey { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }

    // ---- 连接态维度（既有语义，向后兼容） ----
    /// <summary>对外设备状态（5 态，与 GET /api/devices 的 RuntimeStatus 同源同值）。</summary>
    public DeviceStatus Status { get; init; }
    public DeviceConnectionState ConnectionState { get; init; }
    /// <summary>本次连接周期内最近一次连接状态翻转时刻（UTC，可空）。</summary>
    public DateTime? ConnectionStateChangedAt { get; init; }

    // ---- 运行态维度（阶段 2 新增，与连接态正交） ----
    /// <summary>机器运行状态。注意与 Status/ConnectionState 的 Fault 语义区分（方案 P4）：
    /// RunState.Fault = 机器自身故障；Status.Fault = PLC 通信故障。</summary>
    public DeviceRunState RunState { get; init; }
    public DateTime? RunStateChangedAt { get; init; }
    /// <summary>是否存在未恢复报警（不论是否已确认 Ack）。</summary>
    public bool HasAlarm { get; init; }

    // ---- 通信统计 ----
    /// <summary>最近一次采集/连接失败原因（连接周期级，成功后保留至下次覆盖，D3-b）。</summary>
    public string? LastError { get; init; }
    public DateTime? LastCommunicationTime { get; init; }
    public int ConsecutiveFailureCount { get; init; }
    /// <summary>进程启动以来自动重连发起次数（进程级累计，方案 P1/D9-a）。</summary>
    public int ReconnectCount { get; init; }
    public DateTime? LastReconnectAt { get; init; }
    public double AverageResponseTimeMs { get; init; }
    public long SuccessCount { get; init; }
    public long FailureCount { get; init; }
    public long PollRoundCount { get; init; }

    // ---- 规模信息 ----
    /// <summary>设备启用变量数（不含禁用）。</summary>
    public int EnabledVariableCount { get; init; }

    /// <summary>设备变量总数。</summary>
    public int VariableCount { get; init; }

    // ---- 值概要（D4-1：本轮不含变量值；字段预留给 includeValues 扩展） ----
    // public IReadOnlyList<RuntimeVariableSnapshotDto>? Values { get; init; }
}

/// <summary>变量级快照（预留，D4-2 启用时补充，含 Key/Value/Quality/UpdateTime + 上限截断）。</summary>
public class RuntimeVariableSnapshotDto
{
    public string VariableKey { get; init; } = string.Empty;
    public object? Value { get; init; }
    public VariableQuality Quality { get; init; }
    public DateTime? UpdateTime { get; init; }
}