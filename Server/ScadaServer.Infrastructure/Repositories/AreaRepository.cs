
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class AreaRepository : RepositoryBase<Area, int>, IAreaRepository
    {

        public AreaRepository(ScadaDbContext db) : base(db)
        {

        }

    }
}