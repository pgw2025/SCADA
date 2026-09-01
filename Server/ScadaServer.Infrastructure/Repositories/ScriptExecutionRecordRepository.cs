using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 脚本执行记录仓储实现（主键为 long，数据量大，对应表 ScriptExecutionRecords）。用于前端控制台追溯与后台清理。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IScriptExecutionRecordRepository"/> 暴露给上层。
    /// </summary>
    public class ScriptExecutionRecordRepository :
        RepositoryBase<ScriptExecutionRecord, long>,
        IScriptExecutionRecordRepository
    {
        public ScriptExecutionRecordRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 分页查询指定脚本的执行记录。
        /// </summary>
        /// <param name="scriptId">脚本 ID，必填，用于按脚本筛选执行记录。</param>
        /// <param name="result">执行结果（如 Success/Failed），可为空；非空时做精确过滤。</param>
        /// <param name="pageIndex">页码，从 1 开始。</param>
        /// <param name="pageSize">每页条数。</param>
        /// <returns>元组：Total 为符合条件的总条数（用于前端分页），Items 为本页记录列表。</returns>
        public async Task<(int Total, List<ScriptExecutionRecord> Items)> QueryByScriptAsync(
            int scriptId,
            string? result,
            int pageIndex,
            int pageSize)
        {
            // 只读查询使用 AsNoTracking，避免变更跟踪开销；记录量大，性能更优。
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

        /// <summary>
        /// 批量删除指定截止时间之前的执行记录（供后台清理任务调用）。
        /// </summary>
        /// <param name="cutoff">截止时间，仅删除 StartedAt 早于该时间的记录。</param>
        /// <param name="batchSize">单批最多删除的条数。</param>
        /// <returns>本次实际删除的行数；若该批满足条件记录为空则返回 0。</returns>
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