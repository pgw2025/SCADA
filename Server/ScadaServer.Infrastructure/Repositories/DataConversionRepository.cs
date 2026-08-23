using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DataConversionRepository : RepositoryBase<DataConversion, int>, IDataConversionRepository
    {
        public DataConversionRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}