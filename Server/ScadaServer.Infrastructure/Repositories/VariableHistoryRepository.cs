using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 变量历史数据仓储实现（对应表 VariableHistory，主键为 long，数据量大，只读查询）。用于历史趋势/报表等数据追溯。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IVariableHistoryRepository"/> 暴露给上层。
    /// </summary>
    public class VariableHistoryRepository : RepositoryBase<VariableHistory, long>, IVariableHistoryRepository
    {
        public VariableHistoryRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 查询指定变量的最新实时采样记录，按时间倒序取前 limit 条。
        /// </summary>
        /// <param name="deviceKey">设备标识，非空时限定到该设备，避免同名变量跨设备混入。</param>
        /// <param name="variableKey">变量标识，必填。</param>
        /// <param name="limit">最多返回的记录条数。</param>
        /// <param name="start">起始时间下限（闭区间），可选。</param>
        /// <param name="end">结束时间上限（闭区间），可选。</param>
        /// <returns>按时间倒序排列的最多 limit 条历史记录。</returns>
        public async Task<List<VariableHistory>> GetLatestAsync(
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start = null,
            DateTime? end = null)
        {
            var query = Db.VariableHistories.AsNoTracking();

            // 按设备区分同名变量：有设备上下文时限定 device_key，避免跨设备数据混入。
            if (!string.IsNullOrWhiteSpace(deviceKey))
            {
                query = query.Where(h => h.DeviceKey == deviceKey);
            }

            query = query.Where(h => h.VariableKey == variableKey);

            if (start.HasValue)
            {
                query = query.Where(h => h.Timestamp >= start.Value);
            }
            if (end.HasValue)
            {
                query = query.Where(h => h.Timestamp <= end.Value);
            }

            return await query
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// 从指定主键之后增量拉取一批历史记录（用于历史库增量同步/导出，按键升序）。
        /// </summary>
        /// <param name="afterId">下一条起始 Id（不包含，仅取 Id 大于该值的记录）。</param>
        /// <param name="size">批量条数上限。</param>
        /// <returns>按 Id 升序的最多 size 条历史记录。</returns>
        public async Task<List<VariableHistory>> GetBatchAfterIdAsync(long afterId, int size)
        {
            return await Db.VariableHistories
                .AsNoTracking()
                .Where(h => h.Id > afterId)
                .OrderBy(h => h.Id)
                .Take(size)
                .ToListAsync();
        }
    }
}
