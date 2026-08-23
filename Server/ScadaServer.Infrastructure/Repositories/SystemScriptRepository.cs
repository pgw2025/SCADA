using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SystemScriptRepository : RepositoryBase<SystemScript, int>, ISystemScriptRepository
    {
        public SystemScriptRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}