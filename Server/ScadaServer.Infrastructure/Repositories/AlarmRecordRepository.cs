using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 报警记录仓储实现，对应数据库表 <c>AlarmRecords</c>（主键为 long 自增）。
    /// <para>
    /// 承载报警流水/事件的查询与批量兜底更新：按设备/级别/确认与恢复状态等条件分页查询，
    /// 定位未恢复报警记录，以及设备删除时批量标记未恢复报警为已恢复。
    /// </para>
    /// </summary>
    public class AlarmRecordRepository : RepositoryBase<AlarmRecord, long>, IAlarmRecordRepository
    {
        public AlarmRecordRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 组合条件分页查询报警记录，同时返回满足条件的总条数与当前页数据。
        /// <para>必选条件是 <paramref name="pageIndex"/> 与 <paramref name="pageSize"/>（调用方保证均为正数）；其余为可选过滤条件，为空时跳过对应筛选。</para>
        /// </summary>
        /// <param name="deviceId">按所属设备过滤；为空则不过滤。</param>
        /// <param name="level">按报警级别过滤；为空则不过滤。</param>
        /// <param name="unacked">为 true 时仅返回"未确认"记录；false 或 null 不加该条件。</param>
        /// <param name="unrecovered">为 true 时仅返回"未恢复"记录；false 或 null 不加该条件。</param>
        /// <param name="startTime">触发时间下界（TriggeredAt &gt;= startTime）；为空则不过滤。</param>
        /// <param name="endTime">触发时间上界（TriggeredAt &lt;= endTime)；为空则不过滤。</param>
        /// <param name="pageIndex">页码，从 1 开始。</param>
        /// <param name="pageSize">每页条数。</param>
        /// <returns>元组：Total 为满足条件的总记录数，Items 为按触发时间倒序取出的当前页记录。</returns>
        public async Task<(int Total, List<AlarmRecord> Items)> QueryAsync(
            int? deviceId,
            AlarmLevelEnum? level,
            bool? unacked,
            bool? unrecovered,
            DateTime? startTime,
            DateTime? endTime,
            int pageIndex,
            int pageSize)
        {
            // AsNoTracking：本查询只读不修改追踪实体，跳过变更跟踪开销以提升查询性能。
            var query = Db.AlarmRecords.AsNoTracking();

            // 以下按可选条件逐条追加过滤，均为推送至数据库执行的延迟查询，未命中则保持原状。

            if (deviceId.HasValue)
            {
                query = query.Where(r => r.DeviceId == deviceId.Value);
            }

            if (level.HasValue)
            {
                query = query.Where(r => r.Level == level.Value);
            }

            if (unacked.HasValue && unacked.Value)
            {
                query = query.Where(r => !r.Acked);
            }

            if (unrecovered.HasValue && unrecovered.Value)
            {
                query = query.Where(r => r.RecoveredAt == null);
            }

            if (startTime.HasValue)
            {
                query = query.Where(r => r.TriggeredAt >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                query = query.Where(r => r.TriggeredAt <= endTime.Value);
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(r => r.TriggeredAt)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (total, items);
        }

        /// <summary>
        /// 在指定设备 + 变量下查找"仍处于未恢复状态"的最近一次报警记录主键，供恢复报警时按规则定位原始告警。
        /// <para>通过 <paramref name="ruleId"/> 区分两种告警来源：有规则告警（按具体规则匹配）与 Min/Max 上下限兜底告警（RuleId 为 null）。</para>
        /// </summary>
        /// <param name="deviceId">所属设备ID。</param>
        /// <param name="variableKey">变量业务键。</param>
        /// <param name="ruleId">命中规则ID；有值时匹配该规则，无值时仅匹配兜底（即规则 ID 为 null 的记录）。</param>
        /// <returns>命中的未恢复报警主键；未找到返回 null。</returns>
        public async Task<long?> FindUnrecoveredIdAsync(int deviceId, string variableKey, long? ruleId)
        {
            // AsNoTracking：只读定位操作，无需跟踪实体。过滤条件固定为：同设备、同变量、且尚未恢复。
            var query = Db.AlarmRecords
                .AsNoTracking()
                .Where(r => r.DeviceId == deviceId && r.VariableKey == variableKey && r.RecoveredAt == null);

            // 有规则告警严格匹配 RuleId；兜底告警（如 Min/Max 上下限）RuleId 为 null，二者用不同分支确保各按各的来源定位，避免串辈匹配。
            if (ruleId.HasValue)
            {
                query = query.Where(r => r.RuleId == ruleId.Value);
            }
            else
            {
                query = query.Where(r => r.RuleId == null);
            }

            // 触发时间倒序取最早一条未恢复记录；因是查询投影主键，仅 Select 出 Id 以减少数据回传。
            return await query
                .OrderByDescending(r => r.TriggeredAt)
                .Select(r => (long?)r.Id)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// 批量将某设备下所有"未恢复"报警记录标记为已恢复（恢复时间置为 <paramref name="recoverAt"/>），返回受影响行数。
        /// </summary>
        /// <param name="deviceId">被删除/要批量恢复的设备ID。</param>
        /// <param name="recoverAt">统一的恢复时间戳。</param>
        /// <returns>被更新的报警记录行数。</returns>
        public async Task<int> RecoverByDeviceAsync(int deviceId, DateTime recoverAt)
        {
            // 设备删除联动兜底：设备将被删除，其所有未恢复报警不可能再收到"真实恢复"事件，
            // 一次性批量标记为已恢复，避免遗留幽灵未恢复告警（确认状态保持不动）。
            var affected = await Db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE `AlarmRecords` SET `RecoveredAt` = {recoverAt} WHERE `DeviceId` = {deviceId} AND `RecoveredAt` IS NULL");
            return affected;
        }
    }
}