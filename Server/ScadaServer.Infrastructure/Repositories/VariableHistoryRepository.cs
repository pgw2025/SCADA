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
        public async Task<List<VariableHistory>> GetLatestAsync(string variableKey, int limit)
        {
            return await Db.VariableHistories
                .Where(h => h.VariableKey == variableKey)
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .ToListAsync();
        }
    }
}
