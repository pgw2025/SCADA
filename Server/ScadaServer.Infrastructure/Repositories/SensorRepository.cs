using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SensorRepository : RepositoryBase<Sensor, int>, ISensorRepository
    {
        public SensorRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}