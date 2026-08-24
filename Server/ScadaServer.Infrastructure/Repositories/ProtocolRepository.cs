using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ProtocolRepository : RepositoryBase<Protocol, int>, IProtocolRepository
    {
        public ProtocolRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}