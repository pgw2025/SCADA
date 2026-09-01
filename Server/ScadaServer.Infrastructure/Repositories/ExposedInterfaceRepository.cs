using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 暴露接口仓储实现。
    /// 对应实体 <see cref="ExposedInterface"/>，数据表 ExposedInterfaces（对外暴露的设备接口：
    /// 含接口名称、路由 URL、请求方法、暴露键及是否启用等）。
    /// 直接复用基类 <see cref="RepositoryBase{TEntity,TKey}"/> 的通用增删改查，无自定义查询逻辑。
    /// </summary>
    public class ExposedInterfaceRepository : RepositoryBase<ExposedInterface, int>, IExposedInterfaceRepository
    {
        /// <summary>
        /// 初始化暴露接口仓储。
        /// </summary>
        /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
        public ExposedInterfaceRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}