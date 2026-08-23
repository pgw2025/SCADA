using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SystemUserRepository : RepositoryBase<SystemUser, int>, ISystemUserRepository
    {
        public SystemUserRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}