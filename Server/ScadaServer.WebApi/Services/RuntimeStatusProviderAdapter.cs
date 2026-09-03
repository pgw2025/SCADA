using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Interface;

namespace ScadaServer.WebApi.Services;

/// <summary>
/// 将运行时层 RuntimeManager 的运行时状态查询适配为应用层 IRuntimeStatusProvider。
/// 注册为 Singleton，与 RuntimeManager（Singleton）生命周期一致。
/// </summary>
public class RuntimeStatusProviderAdapter : IRuntimeStatusProvider
{
    private readonly IRuntimeManager _runtimeManager;

    public RuntimeStatusProviderAdapter(IRuntimeManager runtimeManager)
    {
        _runtimeManager = runtimeManager;
    }

    public bool TryGetRuntimeStatus(int deviceId, out DeviceStatus status)
    {
        return _runtimeManager.TryGetRuntimeStatus(deviceId, out status);
    }

    public bool TryGetRuntimeSnapshot(int deviceId, out DeviceRuntimeSnapshotDto? snapshot)
    {
        return _runtimeManager.TryGetRuntimeSnapshot(deviceId, out snapshot);
    }
}
