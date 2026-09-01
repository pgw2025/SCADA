
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 区域仓储实现，对应数据库表 <c>Areas</c>（主键为 int）。
    /// <para>
    /// 承载"区域"（设备分组管理）的通用增删改查数据访问操作，由基类 <see cref="RepositoryBase{TEntity, TKey}"/> 提供，
    /// 本类无自定义逻辑；区域编码（Area.Code）用于设备编号自动生成的前缀。
    /// </para>
    /// </summary>
    public class AreaRepository : RepositoryBase<Area, int>, IAreaRepository
    {

        public AreaRepository(ScadaDbContext db) : base(db)
        {

        }

    }
}