using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DataModelRepository : RepositoryBase<DataModel, int>, IDataModelRepository
    {
        public DataModelRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}