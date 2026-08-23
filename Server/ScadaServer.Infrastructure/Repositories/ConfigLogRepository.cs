using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ConfigLogRepository : RepositoryBase<ConfigLog, int>, IConfigLogRepository
    {
        public ConfigLogRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}