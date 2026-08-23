using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SystemLogRepository : RepositoryBase<SystemLog, int>, ISystemLogRepository
    {
        public SystemLogRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}