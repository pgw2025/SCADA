using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class MqttVariableConfigRepository : RepositoryBase<MqttVariableConfig, int>, IRepository<MqttVariableConfig, int>
    {
        public MqttVariableConfigRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}
