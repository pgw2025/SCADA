using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 脚本执行记录仓储实现（主键为 long，数据量大）。用于前端控制台追溯与后台清理。
    /// </summary>
    public class ScriptExecutionRecordRepository :
        RepositoryBase<ScriptExecutionRecord, long>,
        IScriptExecutionRecordRepository
    {
        public ScriptExecutionRecordRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <inheritdoc/>
        public async Task<(int Total, List<ScriptExecutionRecord> Items)> QueryByScriptAsync(
            int scriptId,
            string? result,
            int pageIndex,
            int pageSize)
        {
            var query = Db.Set<ScriptExecutionRecord>().AsNoTracking();
            query = query.Where(r => r.ScriptId == scriptId);

            if (!string.IsNullOrWhiteSpace(result))
            {
                query = query.Where(r => r.Result == result);
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(r => r.StartedAt)
                .ThenByDescending(r => r.Id)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (total, items);
        }

        /// <inheritdoc/>
        public async Task<int> DeleteOlderThanAsync(DateTime cutoff, int batchSize)
        {
            // 分批删除，避免单条 DELETE 过大锁定表影响运行时写入。
            var ids = await Db.Set<ScriptExecutionRecord>()
                .AsNoTracking()
                .Where(r => r.StartedAt < cutoff)
                .OrderBy(r => r.Id)
                .Select(r => (long?)r.Id)
                .Take(batchSize)
                .ToListAsync();

            if (ids.Count == 0)
            {
                return 0;
            }

            var idList = ids.Select(i => i!.Value).ToList();
            var entities = idList.Select(id => new ScriptExecutionRecord { Id = id }).ToList();
            Db.Set<ScriptExecutionRecord>().RemoveRange(entities);
            return await Db.SaveChangesAsync();
        }
    }
}