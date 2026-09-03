using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Runtime.Devices;

/// <summary>
/// 设备运行时对象（即"RuntimeDevice"）。
/// 启动时由 RuntimeManager 依据以下链路构建：
/// Device → DataModel(→Protocol) → DataPointMapping(→DataPoint)。
/// 持有设备实体、数据模型、协议、配置、驱动实例，以及解析后的变量运行时集合。
/// </summary>
public class DeviceRuntime : IRuntimeDevice
{
    private readonly Device _device;

    // 设备实体
    public Device Device { get; init; }

    // ===================== IRuntimeDevice 显式成员 =====================
    /// <summary>设备 ID（IRuntimeDevice）。</summary>
    public int Id => Device.Id;

    /// <summary>设备业务键（IRuntimeDevice）。</summary>
    public string Key => Device.Key;

    /// <summary>
    /// 设备连接配置 JSON（IRuntimeDevice，阶段 6 起单真相源）。
    /// <para>
    /// 配置一律来自设备默认连接行：<c>Device.Connection.ConfigJson</c>；
    /// 阶段 6 已删除对 <c>Device.JsonConfig</c> 的运行时回退（历史列 6.3 起不再写入，
    /// 应用层亦停止双写），连接缺失时兜底 "{}" 交由驱动自行判定。
    /// </para>
    /// </summary>
    public string ConfigJson => Device.Connection?.ConfigJson ?? "{}";

    /// <summary>变量运行时只读视图（IRuntimeDevice 显式实现，驱动仅可遍历不可改集合）。</summary>
    IEnumerable<IRuntimeVariable> IRuntimeDevice.Variables => Variables.Values;

    // 所属数据模型
    public DataModel Model { get; init; } = null!;

    // 协议实体（阶段 6 起：仅来自 Device.Connection.Protocol；DataModel.Protocol 不再作为运行时回退源）
    public Protocol? Protocol { get; init; }

    // 区域（可能未加载，可为 null）
    public Area? Area { get; init; }

    // 驱动实例
    public IProtocolDriver Driver { get; set; } = null!;

    // 变量运行时集合（key = DataPointMapping.Id）
    public Dictionary<int, VariableRuntime> Variables { get; } = new();

    // 通信状态
    private DeviceConnectionState _connectionState;
    public DeviceConnectionState ConnectionState
    {
        get => _connectionState;
        set
        {
            if (_connectionState == value) return;
            _connectionState = value;
            // 仅值变化时推进状态翻转时刻（UTC），供观测「本次连接周期内最近一次翻转」
            ConnectionStateChangedAt = DateTime.UtcNow;
            // 状态变更时通知运行时管理器，用于实时推送与持久化
            ConnectionStateChanged?.Invoke(Device.Id, value);
        }
    }

    /// <summary>
    /// 连接状态变更事件，由 RuntimeManager 订阅以驱动状态推送与落库。
    /// 参数为 (设备ID, 新的连接状态)。
    /// </summary>
    public event Action<int, DeviceConnectionState>? ConnectionStateChanged;

    // 是否正在运行
    public bool IsRunning { get; set; }

    /// <summary>
    /// 待重连标记：连接失败时注册的占位运行时为 true。
    /// 调度器发现该标记后不派发采集 Worker，而是按退避窗口触发运行时管理器重连。
    /// </summary>
    public bool NeedsReconnect { get; set; }

    /// <summary>
    /// 派发/注销串行化锁：调度器"校验在册 → 置位 → 建 token → 记录任务"的派发临界区
    /// 与运行时管理器的设备注销临界区共用，消除"注销后仍被派发"的竞态。
    /// 与采集锁 <see cref="Lock"/> 相互独立，避免相互阻塞。
    /// </summary>
    public object DispatchSync { get; } = new();

    // 最后一次通讯时间
    public DateTime? LastCommunicationTime { get; set; }

    // 最近一次采集时间
    public DateTime? LastPollTime { get; set; }

    // 连续失败次数
    public int ConsecutiveFailureCount { get; set; }

    // 成功次数
    public long SuccessCount { get; set; }

    // 失败次数
    public long FailureCount { get; set; }

    // 采集轮次总数（成功 + 失败），平均响应时间的移动平均分母
    public long PollRoundCount { get; set; }

    // 平均响应时间
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// 最近一次采集/连接失败的原因（截断至 500 字符）。
    /// 生命周期：连接周期级——runtime 重建（重连/重载）即清空；
    /// 采集成功后不清空（D3-b：保留最近错误利于排障），由下次失败覆盖。
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// 本次连接周期内最近一次连接状态翻转时刻（UTC）。
    /// 由 ConnectionState setter 在值变化分支推进；runtime 重建后为 null，直至首次状态变化。
    /// 注意：与进程级 ReconnectCount（RuntimeManager 维护）生命周期不同，见方案 P1。
    /// </summary>
    public DateTime? ConnectionStateChangedAt { get; set; }

    // ===================== 设备运行状态（阶段 2 新增，与连接态正交） =====================

    private DeviceRunState _runState = DeviceRunState.Unknown;
    private int _activeAlarmCount;

    /// <summary>
    /// 机器运行状态（Unknown/Stopped/Running/Paused/Fault/Maintenance）。
    /// 与 ConnectionState（PLC 通信态）正交：Online+Stopped、Fault+Running 均为合法组合。
    /// 持久化于 Devices.RunState，启动/重载时回读初始化（方案 P3）。
    /// </summary>
    public DeviceRunState RunState
    {
        get => _runState;
        private set { /* 经 SetRunState / RestoreRunState 收口 */ }
    }

    /// <summary>RunState 最近一次变更时刻（UTC）。仅经 SetRunState / RestoreRunState 推进。</summary>
    public DateTime? StateChangedAt { get; private set; }

    /// <summary>
    /// 设备级活跃（未恢复）报警计数。Worker FireEvent 打点 ±1；重建时从 AlarmRecords 初始化（方案 P2）。
    /// </summary>
    public int ActiveAlarmCount
    {
        get => _activeAlarmCount;
        private set { /* 经 ApplyAlarmDelta / InitializeAlarmCount 收口 */ }
    }

    /// <summary>是否存在未恢复报警（派生只读）。</summary>
    public bool HasAlarm => _activeAlarmCount > 0;

    /// <summary>
    /// 置位机器运行状态：仅值变化时更新并推进 StateChangedAt。
    /// 不触发任何推送（D10-a：RunState 变化不进 SignalR，由快照轮询读取）。
    /// </summary>
    public void SetRunState(DeviceRunState state)
    {
        if (_runState == state) return;
        _runState = state;
        StateChangedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 报警计数增量打点（Worker FireEvent 出口调用）：Triggered 传 +1，Recovered 传 -1。
    /// 下界 clamp 0，重复 Recovered 不会产生负计数。
    /// </summary>
    public void ApplyAlarmDelta(int delta)
    {
        var newValue = _activeAlarmCount + delta;
        _activeAlarmCount = newValue < 0 ? 0 : newValue;
    }

    /// <summary>
    /// 报警计数初始化（BuildAndRegisterDeviceAsync 重建 runtime 时从 AlarmRecords 查询结果回填）。
    /// 与 ApplyAlarmDelta 互斥使用：仅注册路径调用一次。
    /// </summary>
    public void InitializeAlarmCount(int count)
    {
        _activeAlarmCount = count < 0 ? 0 : count;
    }

    /// <summary>注册路径专用：从持久化列恢复 RunState 与变更时刻（不经 SetRunState 的时间戳刷新）。</summary>
    public void RestoreRunState(DeviceRunState state, DateTime? changedAt)
    {
        _runState = state;
        StateChangedAt = changedAt;
    }

    // 运行时锁
    public SemaphoreSlim Lock { get; } = new(1, 1);

    // Worker 独立取消源（链接全局关停令牌，支持单设备启停）
    private CancellationTokenSource? _workerCts;

    /// <summary>
    /// 当前 Worker 的任务句柄（由调度器派发时在 DispatchSync 锁内记录，
    /// 供单设备停止时等待其收尾）。
    /// 注意：Worker 退出时不再置空——始终指向最近一次派发的 Worker，
    /// 避免旧 Worker 的退出清理覆盖新 Worker 的句柄；等待已完成的旧任务无副作用。
    /// </summary>
    public Task? WorkerTask { get; set; }

    /// <summary>
    /// 创建当前 Worker 的取消源：链接全局关停令牌（调度器/宿主），
    /// 同时允许对单台设备独立取消（用于运行期注销/重载该设备）。
    /// 必须在 <see cref="DispatchSync"/> 锁内调用（由调度器保证）。
    /// </summary>
    /// <param name="globalToken">全局关停令牌（调度器链接宿主 token 的令牌）</param>
    /// <returns>属于本次派发的取消源（调用方持有引用用于退出时的所有权校验）</returns>
    public CancellationTokenSource CreateWorkerCts(CancellationToken globalToken)
    {
        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);
        return _workerCts;
    }

    /// <summary>
    /// 取消当前 Worker（单设备注销/重载时调用），触发其干净退出。
    /// </summary>
    public void CancelWorker()
    {
        _workerCts?.Cancel();
    }

    /// <summary>
    /// 释放 Worker 取消源（带所有权校验）：仅当当前取消源仍属于指定 Worker 时才释放，
    /// 避免旧 Worker 退出清理时误释放/覆盖新 Worker 的取消源。
    /// 应在 Worker 退出时由调度器调用。
    /// </summary>
    /// <param name="cts">该 Worker 派发时持有的取消源引用</param>
    public void DisposeWorkerTokenIfCurrent(CancellationTokenSource cts)
    {
        if (!ReferenceEquals(_workerCts, cts))
        {
            return;
        }

        _workerCts = null;
        cts.Dispose();
    }

    public DeviceRuntime(Device device)
    {
        _device = device;
        Device = device;
    }
}
