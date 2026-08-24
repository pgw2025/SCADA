using ScadaServer.Domain.Entities;

namespace ScadaServer.Domain.Interfaces.Repositories;

/// <summary>
/// 设备变量仓储接口
/// </summary>
public interface IDeviceVariableRepository : IRepository<DeviceVariable, int> { }
