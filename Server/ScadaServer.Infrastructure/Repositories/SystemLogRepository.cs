using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class SystemLogRepository : RepositoryBase<SystemLog, int>, ISystemLogRepository
    {
        public SystemLogRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <inheritdoc/>
        public async Task<(int Total, List<SystemLog> Items)> QueryAsync(
            string? category,
            List<string>? levels,
            string? keyword,
            string? source,
            DateTime? startTime,
            DateTime? endTime,
            int pageIndex,
            int pageSize)
        {
            var query = Db.SystemLogs.AsQueryable();

            // 分类过滤（命中 (Category, Timestamp) 复合索引左前缀）
            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(l => l.Category == category);
            }

            // 级别多选过滤
            var levelSet = levels?
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (levelSet is { Count: > 0 })
            {
                query = query.Where(l => levelSet.Contains(l.Level));
            }

            // 来源精确过滤
            if (!string.IsNullOrWhiteSpace(source))
            {
                query = query.Where(l => l.Source == source);
            }

            // 时间范围（闭区间）
            if (startTime.HasValue)
            {
                query = query.Where(l => l.Timestamp >= startTime.Value);
            }
            if (endTime.HasValue)
            {
                query = query.Where(l => l.Timestamp <= endTime.Value);
            }

            // 关键字：Content / Source / Operator 三字段模糊匹配（EF.Functions.Like + 转义 % _ \，防注入）
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = EscapeLike(keyword.Trim());
                var pattern = $"%{kw}%";
                query = query.Where(l =>
                    EF.Functions.Like(l.Content, pattern) ||
                    EF.Functions.Like(l.Source, pattern) ||
                    (l.Operator != null && EF.Functions.Like(l.Operator, pattern)));
            }

            // 总数
            var total = await query.CountAsync();

            // 排序稳定性：时间倒序 + 主键倒序，避免同秒多条跨页重复/乱序
            var items = await query
                .OrderByDescending(l => l.Timestamp)
                .ThenByDescending(l => l.Id)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (total, items);
        }

        /// <inheritdoc/>
        public async Task<int> ClearAsync(string? category, DateTime? startTime, DateTime? endTime)
        {
            var query = Db.SystemLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(l => l.Category == category);
            }
            if (startTime.HasValue)
            {
                query = query.Where(l => l.Timestamp >= startTime.Value);
            }
            if (endTime.HasValue)
            {
                query = query.Where(l => l.Timestamp <= endTime.Value);
            }

            // 使用 EF Core 7+ 的 ExecuteDeleteAsync，单条 SQL 批量删除，不逐行加载。
            return await query.ExecuteDeleteAsync();
        }

        /// <summary>
        /// 转义 LIKE 通配符，避免用户输入 % 或 _ 被当作通配符。
        /// </summary>
        private static string EscapeLike(string input)
        {
            return input
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
        }
    }
}
