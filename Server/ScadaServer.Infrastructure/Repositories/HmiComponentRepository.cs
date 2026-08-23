using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class HmiComponentRepository : RepositoryBase<HmiComponent, int>, IHmiComponentRepository
    {
        public HmiComponentRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}