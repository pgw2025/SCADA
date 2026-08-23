using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class MqttServerRepository : RepositoryBase<MqttServer, int>, IMqttServerRepository
    {
        public MqttServerRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}