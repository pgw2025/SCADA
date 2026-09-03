using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories;

/// <summary>
/// 设备变量仓储实现。
/// 对应实体 <see cref="DataPointMapping"/>，数据表 DataPointMappings（描述某个模型变量在具体设备上的实例化实现：
/// 实际寄存器地址、位偏移、轮询间隔及缩放 / 死区覆盖值等）。
/// 直接复用基类 <see cref="RepositoryBase{TEntity,TKey}"/> 的通用增删改查，无自定义查询逻辑。
/// </summary>
public class DataPointMappingRepository : RepositoryBase<DataPointMapping, int>, IDataPointMappingRepository
{
    /// <summary>
    /// 初始化设备变量仓储。
    /// </summary>
    /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
    public DataPointMappingRepository(ScadaDbContext db) : base(db)
    {
    }
}
