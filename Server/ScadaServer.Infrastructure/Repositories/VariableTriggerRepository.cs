using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class VariableTriggerRepository : RepositoryBase<VariableTrigger, int>, IVariableTriggerRepository
    {
        public VariableTriggerRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}