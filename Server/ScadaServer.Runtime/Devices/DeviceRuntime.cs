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
public class DeviceRuntime
{
    private readonly Device _device;

    // 设备实体
    public Device Device { get; init; }

    // 所属数据模型
    public DataModel Model { get; init; }

    // 协议实体（来自 DataModel.Protocol，可能为 null，此时回退到 Model.Type 派发驱动）
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

    // 取消令牌
    public CancellationTokenSource? CancellationTokenSource { get; set; }

    private CancellationTokenSource?
        _cts;

    private Task?
        _workerTask;

    public DeviceRuntime(Device device)
    {
        _device = device;
        Device = device;
    }
}
