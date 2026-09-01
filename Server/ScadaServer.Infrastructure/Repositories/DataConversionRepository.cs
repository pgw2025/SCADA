using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 数据转换仓储实现，对应数据库表 <c>DataConversions</c>（主键为 int）。
    /// <para>
    /// 承载"数据转换规则"（变量间的数据转发，含源设备/源变量到目标设备/目标变量的映射）的通用增删改查
    /// 数据访问操作，由基类 <see cref="RepositoryBase{TEntity, TKey}"/> 提供，本类无自定义逻辑。
    /// </para>
    /// </summary>
    public class DataConversionRepository : RepositoryBase<DataConversion, int>, IDataConversionRepository
    {
        public DataConversionRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}