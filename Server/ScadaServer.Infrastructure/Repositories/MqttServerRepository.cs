using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class MqttServerRepository : RepositoryBase<MqttServer, int>, IMqttServerRepository
    {
        public MqttServerRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}