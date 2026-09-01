using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 系统配置仓储实现，对应表 SystemConfig，用于读写系统级键值配置项。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="ISystemConfigRepository"/> 暴露给上层。
    /// </summary>
    public class SystemConfigRepository : RepositoryBase<SystemConfig, int>, ISystemConfigRepository
    {
        public SystemConfigRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}