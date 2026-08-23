using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DeviceConfigRepository : RepositoryBase<DeviceConfig, int>, IRepository<DeviceConfig, int>
    {
        public DeviceConfigRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}
