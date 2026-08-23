using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ExposedInterfaceRepository : RepositoryBase<ExposedInterface, int>, IExposedInterfaceRepository
    {
        public ExposedInterfaceRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}