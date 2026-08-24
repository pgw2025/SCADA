using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories;

/// <summary>
/// 设备变量仓储实现
/// </summary>
public class DeviceVariableRepository : RepositoryBase<DeviceVariable, int>, IDeviceVariableRepository
{
    public DeviceVariableRepository(ScadaDbContext db) : base(db)
    {
    }
}
