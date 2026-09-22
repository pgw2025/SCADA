using System.Text;
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
        /// 幂等批量插入历史采样点：INSERT ... ON DUPLICATE KEY UPDATE Id=Id（保留首条，S1）。
        /// 依赖 (VariableKey, Timestamp, DeviceId) 唯一索引，同设备同变量同刻重复不抛 1062、静默跳过。
        /// </summary>
        public async Task<int> InsertIdempotentAsync(IReadOnlyList<VariableHistory> points, CancellationToken token)
        {
            if (points == null || points.Count == 0)
            {
                return 0;
            }

            const int colCount = 8;
            var sb = new StringBuilder();
            sb.Append("INSERT INTO `VariableHistory` (`DeviceId`, `DeviceKey`, `VariableKey`, `VariableName`, `Value`, `RawValue`, `Timestamp`, `Quality`) VALUES ");

            var parameters = new object[points.Count * colCount];
            for (var i = 0; i < points.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                var p = points[i];
                var baseIdx = i * colCount;
                parameters[baseIdx] = p.DeviceId;
                parameters[baseIdx + 1] = p.DeviceKey;
                parameters[baseIdx + 2] = p.VariableKey;
                parameters[baseIdx + 3] = p.VariableName;
                parameters[baseIdx + 4] = p.Value;
                parameters[baseIdx + 5] = (object?)p.RawValue ?? DBNull.Value;
                parameters[baseIdx + 6] = p.Timestamp;
                parameters[baseIdx + 7] = (object?)p.Quality ?? DBNull.Value;

                sb.Append("({")
                  .Append(baseIdx).Append("}, {")
                  .Append(baseIdx + 1).Append("}, {")
                  .Append(baseIdx + 2).Append("}, {")
                  .Append(baseIdx + 3).Append("}, {")
                  .Append(baseIdx + 4).Append("}, {")
                  .Append(baseIdx + 5).Append("}, {")
                  .Append(baseIdx + 6).Append("}, {")
                  .Append(baseIdx + 7).Append("})");
            }

            sb.Append(" ON DUPLICATE KEY UPDATE `Id` = `Id`");

            return await Db.Database.ExecuteSqlRawAsync(sb.ToString(), (IEnumerable<object>)parameters, token);
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

        /// <inheritdoc/>
        public async Task<long> DeleteBeforeAsync(DateTime cutoffUtc, int batchSize, int batchDelayMs, CancellationToken token)
        {
            long total = 0;
            while (true)
            {
                var deleted = await Db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM `VariableHistory` WHERE `Timestamp` < {cutoffUtc} ORDER BY `Id` LIMIT {batchSize}",
                    token);
                if (deleted <= 0)
                {
                    break;
                }
                total += deleted;
                if (deleted < batchSize)
                {
                    break;
                }
                if (batchDelayMs > 0)
                {
                    await Task.Delay(batchDelayMs, token);
                }
            }
            return total;
        }

        /// <inheritdoc/>
        public async Task<List<VariableHistory>> GetAggregatedAsync(
            string deviceKey,
            string variableKey,
            DateTime? start,
            DateTime? end,
            long windowMs,
            string fn,
            int limit)
        {
            // 窗口向上取整为整数秒，防 windowSec=0 除零；窗口上限 30 天防溢出。
            var windowSec = Math.Max(1L, (windowMs + 999) / 1000);
            if (windowSec > 30L * 24 * 3600)
            {
                windowSec = 30L * 24 * 3600;
            }

            // 聚合函数白名单（杜绝 SQL 注入）。
            var fnNorm = (fn ?? "mean").ToLowerInvariant();
            var isFirst = fnNorm == "first";
            var isLast = fnNorm == "last";
            var isMax = fnNorm == "max";
            var isMin = fnNorm == "min";
            if (!isFirst && !isLast && !isMax && !isMin)
            {
                fnNorm = "mean"; // 非法函数回退 mean
            }

            var hasDevice = !string.IsNullOrWhiteSpace(deviceKey);
            var devKey = hasDevice ? deviceKey : string.Empty;

            // first/last 与 mean/max/min 两条 SQL 分支。聚合函数名（agg）/排序方向（orderDir）为白名单常量，非用户输入。
            // 其余值通过 FromSqlRaw 的位置占位符 {0}..{5}（string.Format 语义）参数化绑定，数组固定 6 元素、占位符连续，
            // 未提供的条件（无 device/无 start/无 end）用「恒真」写法保留占位符位置，杜绝参数错位与 SQL 注入：
            //   {0}=windowSec, {1}=variableKey, {2}=deviceKey, {3}=start, {4}=end, {5}=limit
            // 注意：SQL 模板用普通字符串拼接（非 $ 插值），避免 {0} 被 C# 插值误解析；{0}..{5} 是 string.Format 占位符。
            if (isFirst || isLast)
            {
                var orderDir = isFirst ? "ASC" : "DESC";
                var sql =
                    "WITH b AS (\n" +
                    "    SELECT `Id`, `DeviceId`, `DeviceKey`, `VariableKey`, `VariableName`, `Value`, `RawValue`, `Timestamp`, `Quality`,\n" +
                    "           FLOOR(UNIX_TIMESTAMP(`Timestamp`) / {0}) * {0} AS bucket,\n" +
                    "           ROW_NUMBER() OVER (PARTITION BY FLOOR(UNIX_TIMESTAMP(`Timestamp`) / {0}) * {0} ORDER BY `Timestamp` " + orderDir + ", `Id` " + orderDir + ") AS rn\n" +
                    "    FROM `VariableHistory`\n" +
                    "    WHERE `VariableKey` = {1}\n" +
                    "                      AND ({2} = '' OR `DeviceKey` = {2})\n" +
                    "                      AND `Timestamp` >= {3}\n" +
                    "                      AND `Timestamp` <= {4}\n" +
                    ")\n" +
                    "SELECT `Id`, `DeviceId`, `DeviceKey`, `VariableKey`, `VariableName`, `Value`, `RawValue`, `Timestamp`, `Quality`\n" +
                    "FROM b\n" +
                    "WHERE `rn` = 1\n" +
                    "ORDER BY `bucket` DESC\n" +
                    "LIMIT {5}";
                var parameters = BuildParams(windowSec, variableKey, devKey, start, end, limit);
                return await Db.VariableHistories
                    .FromSqlRaw(sql, parameters)
                    .AsNoTracking()
                    .ToListAsync();
            }

            var agg = isMax ? "MAX(`Value`)" : isMin ? "MIN(`Value`)" : "AVG(`Value`)";
            // ONLY_FULL_GROUP_BY 兼容：SELECT / GROUP BY / ORDER BY 三处必须使用完全一致的桶表达式
            //（统一用 FROM_UNIXTIME(FLOOR(UNIX_TIMESTAMP(`Timestamp`)/{0})*{0})），否则 MySQL 会把
            // 表达式中未聚合的裸 `Timestamp` 列判定为非法，报 Expression #N is not in GROUP BY clause。
            var sqlAgg =
                "SELECT 0 AS `Id`, 0 AS `DeviceId`, '' AS `DeviceKey`, {1} AS `VariableKey`, '' AS `VariableName`,\n" +
                "       " + agg + " AS `Value`, NULL AS `RawValue`,\n" +
                "       FROM_UNIXTIME(FLOOR(UNIX_TIMESTAMP(`Timestamp`) / {0}) * {0}) AS `Timestamp`,\n" +
                "       NULL AS `Quality`\n" +
                "FROM `VariableHistory`\n" +
                "WHERE `VariableKey` = {1}\n" +
                "  AND ({2} = '' OR `DeviceKey` = {2})\n" +
                "  AND `Timestamp` >= {3}\n" +
                "  AND `Timestamp` <= {4}\n" +
                "GROUP BY FROM_UNIXTIME(FLOOR(UNIX_TIMESTAMP(`Timestamp`) / {0}) * {0})\n" +
                "ORDER BY FROM_UNIXTIME(FLOOR(UNIX_TIMESTAMP(`Timestamp`) / {0}) * {0}) DESC\n" +
                "LIMIT {5}";
            var parametersAgg = BuildParams(windowSec, variableKey, devKey, start, end, limit);
            return await Db.VariableHistories
                .FromSqlRaw(sqlAgg, parametersAgg)
                .AsNoTracking()
                .ToListAsync();
        }

        /// <summary>
        /// 构造固定 6 元素的参数数组，下标语义与 SQL 中 {0}..{5} 位置占位符一一对应：
        /// {0}=windowSec, {1}=variableKey, {2}=deviceKey, {3}=start, {4}=end, {5}=limit。
        /// <para>未提供的条件在 SQL 里用「恒真」写法保留占位符位置（如 deviceKey 为空时传 ''，配合
        /// `({2} = '' OR DeviceKey = {2})` 恒真；start 为 null 时传 DateTime.MinValue，`Timestamp >= {3}` 恒真；
        /// end 为 null 时传 DateTime.MaxValue，`Timestamp <= {4}` 恒真），从而保证占位符始终连续、数组长度固定。</para>
        /// </summary>
        private static object[] BuildParams(
            long windowSec,
            string variableKey,
            string deviceKey,
            DateTime? start,
            DateTime? end,
            int limit)
        {
            return new object[]
            {
                windowSec,                                      // {0}
                variableKey,                                    // {1}
                string.IsNullOrEmpty(deviceKey) ? "" : deviceKey, // {2}
                (start.HasValue ? (object)start.Value : DateTime.MinValue), // {3}
                (end.HasValue ? (object)end.Value : DateTime.MaxValue),     // {4}
                limit                                           // {5}
            };
        }
    }
}
