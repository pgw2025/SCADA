using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 配置日志仓储实现，对应数据库表 <c>ConfigLog</c>（主键为 int）。
    /// <para>
    /// 承载"设备配置变更日志"的通用增删改查数据访问操作，由基类 <see cref="RepositoryBase{TEntity, TKey}"/> 提供，
    /// 本类无自定义逻辑，记录设备配置的变更人、变更描述与创建时间，用于操作审计追溯。
    /// </para>
    /// </summary>
    public class ConfigLogRepository : RepositoryBase<ConfigLog, int>, IConfigLogRepository
    {
        public ConfigLogRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}