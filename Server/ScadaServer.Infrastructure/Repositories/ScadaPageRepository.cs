using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ScadaPageRepository : RepositoryBase<ScadaPage, int>, IScadaPageRepository
    {
        public ScadaPageRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}