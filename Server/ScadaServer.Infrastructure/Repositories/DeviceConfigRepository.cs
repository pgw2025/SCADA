using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 设备配置仓储实现。
    /// 对应实体 <see cref="DeviceConfig"/>，数据表 DeviceConfigs（设备协议配置，含 JSON 配置内容与版本号）。
    /// 直接复用基类 <see cref="RepositoryBase{TEntity,TKey}"/> 的通用增删改查，无自定义查询逻辑。
    /// </summary>
    public class DeviceConfigRepository : RepositoryBase<DeviceConfig, int>, IRepository<DeviceConfig, int>
    {
        /// <summary>
        /// 初始化设备配置仓储。
        /// </summary>
        /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
        public DeviceConfigRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}
