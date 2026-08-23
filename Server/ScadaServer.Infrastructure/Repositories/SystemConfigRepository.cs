using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SystemConfigRepository : RepositoryBase<SystemConfig, int>, ISystemConfigRepository
    {
        public SystemConfigRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}