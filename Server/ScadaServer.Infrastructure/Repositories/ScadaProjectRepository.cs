using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ScadaProjectRepository : RepositoryBase<ScadaProject, int>, IScadaProjectRepository
    {
        public ScadaProjectRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}