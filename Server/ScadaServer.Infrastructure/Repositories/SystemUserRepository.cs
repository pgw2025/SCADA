using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SystemUserRepository : RepositoryBase<SystemUser, int>, ISystemUserRepository
    {
        public SystemUserRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}