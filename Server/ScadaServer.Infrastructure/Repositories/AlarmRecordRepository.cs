using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 报警记录仓储实现（主键为 long）。
    /// </summary>
    public class AlarmRecordRepository : RepositoryBase<AlarmRecord, long>, IAlarmRecordRepository
    {
        public AlarmRecordRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <inheritdoc/>
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
            var query = Db.AlarmRecords.AsNoTracking();

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

        /// <inheritdoc/>
        public async Task<long?> FindUnrecoveredIdAsync(int deviceId, string variableKey, long? ruleId)
        {
            var query = Db.AlarmRecords
                .AsNoTracking()
                .Where(r => r.DeviceId == deviceId && r.VariableKey == variableKey && r.RecoveredAt == null);

            if (ruleId.HasValue)
            {
                query = query.Where(r => r.RuleId == ruleId.Value);
            }
            else
            {
                query = query.Where(r => r.RuleId == null);
            }

            return await query
                .OrderByDescending(r => r.TriggeredAt)
                .Select(r => (long?)r.Id)
                .FirstOrDefaultAsync();
        }

        /// <inheritdoc/>
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