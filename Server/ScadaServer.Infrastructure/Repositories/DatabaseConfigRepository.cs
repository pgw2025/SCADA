using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 数据库配置仓储实现，对应数据库表 <c>DatabaseConfigs</c>（主键为 int）。
    /// <para>
    /// 承载外部/历史数据库连接配置（如 InfluxDB 的连接地址、Token、Bucket 等）的通用增删改查数据访问操作，
    /// 由基类 <see cref="RepositoryBase{TEntity, TKey}"/> 提供，本类无自定义逻辑。
    /// </para>
    /// </summary>
    public class DatabaseConfigRepository : RepositoryBase<DatabaseConfig, int>, IDatabaseConfigRepository
    {
        public DatabaseConfigRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}