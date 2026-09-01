using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 传感器仓储实现，对应表 Sensors，用于管理传感器的定义与配置信息。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="ISensorRepository"/> 暴露给上层。
    /// </summary>
    public class SensorRepository : RepositoryBase<Sensor, int>, ISensorRepository
    {
        public SensorRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}