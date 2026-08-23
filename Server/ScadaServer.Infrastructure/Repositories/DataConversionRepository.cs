using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DataConversionRepository : RepositoryBase<DataConversion, int>, IDataConversionRepository
    {
        public DataConversionRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}