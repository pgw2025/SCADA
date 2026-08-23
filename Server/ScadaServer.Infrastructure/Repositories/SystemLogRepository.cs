using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SystemLogRepository : RepositoryBase<SystemLog, int>, ISystemLogRepository
    {
        public SystemLogRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}