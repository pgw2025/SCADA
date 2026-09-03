using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.Interfaces;

/// <summary>
/// 运行时设备状态提供器。
/// 由 WebApi 层用 RuntimeManager 适配实现，避免 Application 层反向依赖 Runtime 程序集。
/// </summary>
public interface IRuntimeStatusProvider
{
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
    /// 尝试获取设备运行时聚合快照（连接态 + 运行态 + 报警 + 统计）。
    /// 设备未注册到运行时（禁用/初始化失败/重连窗口期）返回 false，调用方回退 404（D5-a）。
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="snapshot">聚合快照；未注册时为 null</param>
    /// <returns>设备是否注册到运行中</returns>
    bool TryGetRuntimeSnapshot(int deviceId, out DeviceRuntimeSnapshotDto? snapshot);
}
