using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;


namespace ScadaServer.Infrastructure.Repositories
{
    public class DeviceRepository : RepositoryBase<Device, int>, IDeviceRepository
    {
        public DeviceRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}