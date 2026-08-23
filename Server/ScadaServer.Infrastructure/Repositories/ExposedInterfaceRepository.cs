using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ExposedInterfaceRepository : RepositoryBase<ExposedInterface, int>, IExposedInterfaceRepository
    {
        public ExposedInterfaceRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}