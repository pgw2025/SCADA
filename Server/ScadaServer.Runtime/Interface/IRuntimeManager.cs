using System;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Runtime.Interface;

/// <summary>
/// 设备运行时状态变更事件参数
/// </summary>
public class DeviceStatusChangedEventArgs : EventArgs
{
    public int DeviceId { get; init; }
    public DeviceStatus Status { get; init; }
}

/// <summary>
/// 运行时管理器接口
/// </summary>
public interface IRuntimeManager
{
    /// <summary>
    /// 初始化运行时
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// 启动运行时调度。调度器为每台启用设备派生唯一的常驻 Worker，无需并发度参数。
    /// </summary>
    Task StartAsync(CancellationToken token);

    /// <summary>
    /// 停止运行时调度，优雅退出所有设备工作线程
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 尝试获取设备运行时状态。
    /// 设备未注册到运行时（如已禁用、初始化失败或进程刚启动尚未加载）时返回 false，
    /// 调用方应据此回退为 Offline 等默认值。
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="status">映射后的对外设备状态</param>
    /// <returns>设备是否存在于运行中</returns>
    bool TryGetRuntimeStatus(int deviceId, out DeviceStatus status);

    /// <summary>
    /// 设备运行时状态变更事件。状态由连接态映射而来，仅在对外状态值变化时触发，
    /// 供通知服务推送与持久化订阅者使用。
    /// </summary>
    event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;
}