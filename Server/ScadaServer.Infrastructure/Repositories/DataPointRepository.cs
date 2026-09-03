using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 模型变量仓储实现。
    /// 对应实体 <see cref="DataPoint"/>，数据表 DataPoints（数据模型下的变量模板定义：
    /// 变量键、名称、数据类型、缩放、死区、历史存储模式等）。
    /// 重写了基类的 <c>GetListAsync</c>，对列表查询统一追加稳定排序；其余增删改查复用基类
    /// <see cref="RepositoryBase{TEntity,TKey}"/> 实现。
    /// </summary>
    public class DataPointRepository : RepositoryBase<DataPoint, int>, IDataPointRepository
    {
        /// <summary>
        /// 初始化模型变量仓储。
        /// </summary>
        /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
        public DataPointRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 获取全部模型变量列表。
        /// </summary>
        /// <returns>全部 <see cref="DataPoint"/> 列表，按 ModelId、Key 稳定升序排列。</returns>
        /// <remarks>
        /// 统一约定：模型变量列表按 ModelId、Key 排序，保证返回顺序稳定（消除前端列表抖动）。
        /// AsNoTracking：本查询仅用于展示 / 比对，关闭变更跟踪以降低内存与跟踪开销。
        /// </remarks>
        public override Task<List<DataPoint>> GetListAsync()
            => Db.DataPoints.AsNoTracking().OrderBy(mv => mv.ModelId).ThenBy(mv => mv.Key).ToListAsync();

        /// <summary>
        /// 按条件查询模型变量列表。
        /// </summary>
        /// <param name="predicate">筛选条件表达式（例如按 ModelId 取某模型的变量）。</param>
        /// <returns>满足条件且按 Key 稳定升序排列的 <see cref="DataPoint"/> 列表。</returns>
        /// <remarks>
        /// 带条件查询同样保持稳定排序（如按 ModelId 取变量时按 Key 排序，保证前端展示顺序确定）。
        /// AsNoTracking：仅用于读取，关闭变更跟踪以降低开销。
        /// </remarks>
        public override Task<List<DataPoint>> GetListAsync(Expression<Func<DataPoint, bool>> predicate)
            => Db.DataPoints.AsNoTracking().Where(predicate).OrderBy(mv => mv.Key).ToListAsync();
    }
}