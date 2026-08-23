using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ModelVariableRepository : RepositoryBase<ModelVariable, int>, IModelVariableRepository
    {
        public ModelVariableRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}