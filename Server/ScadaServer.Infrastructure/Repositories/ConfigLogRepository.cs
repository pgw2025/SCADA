using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ConfigLogRepository : RepositoryBase<ConfigLog, int>, IConfigLogRepository
    {
        public ConfigLogRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}