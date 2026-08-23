using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SystemConfigRepository : RepositoryBase<SystemConfig, int>, ISystemConfigRepository
    {
        public SystemConfigRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}