using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ModelVariableRepository : RepositoryBase<ModelVariable, int>, IModelVariableRepository
    {
        public ModelVariableRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}