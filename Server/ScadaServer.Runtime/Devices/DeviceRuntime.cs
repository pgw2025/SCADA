using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Runtime.Devices;

/// <summary>
/// 设备运行时对象（即"RuntimeDevice"）。
/// 启动时由 RuntimeManager 依据以下链路构建：
/// Device → DataModel(→Protocol) → DeviceConfig → DeviceVariable(→ModelVariable)。
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

    /// <summary>设备连接配置 JSON（IRuntimeDevice，来自 DeviceConfig.JsonConfig）。</summary>
    public string ConfigJson => Config?.JsonConfig ?? "{}";

    /// <summary>变量运行时只读视图（IRuntimeDevice 显式实现，驱动仅可遍历不可改集合）。</summary>
    IEnumerable<IRuntimeVariable> IRuntimeDevice.Variables => Variables.Values;

    // 所属数据模型
    public DataModel Model { get; init; }

    // 协议实体（来自 DataModel.Protocol，模型必绑协议后作为驱动派发真相源）
    public Protocol? Protocol { get; init; }

    // 设备配置（设备级协议配置，来自 DeviceConfig）
    public DeviceConfig? Config { get; init; }

    // 区域（可能未加载，可为 null）
    public Area? Area { get; init; }

    // 驱动实例
    public IProtocolDriver Driver { get; set; }

    // 变量运行时集合（key = DeviceVariable.Id）
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

    // 平均响应时间
    public double AverageResponseTime { get; set; }

    // 运行时锁
    public SemaphoreSlim Lock { get; } = new(1, 1);

    // Worker 独立取消源（链接全局关停令牌，支持单设备启停）
    private CancellationTokenSource? _workerCts;

    /// <summary>
    /// 当前 Worker 的任务句柄（由调度器派发时记录，供单设备停止时等待其收尾）。
    /// </summary>
    public Task? WorkerTask { get; set; }

    /// <summary>
    /// 创建当前 Worker 使用的取消令牌：链接全局关停令牌（调度器/宿主），
    /// 同时允许对单台设备独立取消（用于运行期注销/重载该设备）。
    /// </summary>
    /// <param name="globalToken">全局关停令牌（调度器链接宿主 token 的令牌）</param>
    /// <returns>Worker 应监听的取消令牌</returns>
    public CancellationToken CreateWorkerToken(CancellationToken globalToken)
    {
        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);
        return _workerCts.Token;
    }

    /// <summary>
    /// 取消当前 Worker（单设备注销/重载时调用），触发其干净退出。
    /// </summary>
    public void CancelWorker()
    {
        _workerCts?.Cancel();
    }

    /// <summary>
    /// 释放 Worker 取消源，应在 Worker 退出后由调度器调用。
    /// </summary>
    public void DisposeWorkerToken()
    {
        _workerCts?.Dispose();
        _workerCts = null;
    }

    public DeviceRuntime(Device device)
    {
        _device = device;
        Device = device;
    }
}
