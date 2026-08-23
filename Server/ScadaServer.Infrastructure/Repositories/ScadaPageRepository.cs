using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ScadaPageRepository : RepositoryBase<ScadaPage, int>, IScadaPageRepository
    {
        public ScadaPageRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}