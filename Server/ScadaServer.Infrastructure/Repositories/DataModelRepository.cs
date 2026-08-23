using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DataModelRepository : RepositoryBase<DataModel, int>, IDataModelRepository
    {
        public DataModelRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}