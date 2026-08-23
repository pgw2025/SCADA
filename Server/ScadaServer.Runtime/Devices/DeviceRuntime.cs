using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;

namespace ScadaServer.Runtime.Devices;

public class DeviceRuntime
{
    private readonly Device _device;
    // 配置对象
    public Device Device { get; init; }

    // 所属模型
    public DataModel Model { get; init; }

    // 区域
    public Area Area { get; init; }

    // 驱动实例
    public IProtocolDriver Driver { get; set; }

    // 变量列表
    public Dictionary<int, VariableRuntime> Variables { get; }
        = new();

    // 通信状态
    public DeviceConnectionState ConnectionState { get; set; }

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
    public SemaphoreSlim Lock { get; }
        = new(1, 1);

    // 取消令牌
    public CancellationTokenSource? CancellationTokenSource { get; set; }

    private CancellationTokenSource?
        _cts;

    private Task?
        _workerTask;

    public DeviceRuntime(Device device)
    {
        _device = device;

    }

}