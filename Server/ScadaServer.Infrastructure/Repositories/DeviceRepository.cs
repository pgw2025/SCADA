using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;


namespace ScadaServer.Infrastructure.Repositories
{
    public class DeviceRepository : RepositoryBase<Device, int>, IDeviceRepository
    {
        public DeviceRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}