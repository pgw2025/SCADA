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
}
