using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 变量历史数据仓储
    /// </summary>
    public class VariableHistoryRepository : RepositoryBase<VariableHistory, long>, IVariableHistoryRepository
    {
        public VariableHistoryRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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
