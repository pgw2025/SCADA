using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 报警记录应用服务实现（查询/确认/当前未恢复）。报警记录由运行时写入，服务端只读 + 确认操作。
    /// </summary>
    public class AlarmRecordAppService : IAlarmRecordAppService
    {
        /// <summary>报警记录仓储，提供查询、确认等持久化能力。</summary>
        private readonly IAlarmRecordRepository _repository;

        /// <summary>构造函数：注入报警记录仓储。</summary>
        public AlarmRecordAppService(IAlarmRecordRepository repository)
        {
            _repository = repository;
        }

        /// <inheritdoc/>
        public async Task<AlarmRecordPagedResultDto> QueryAsync(AlarmRecordQueryDto query)
        {
            var q = query ?? new AlarmRecordQueryDto();
            var pageIndex = q.PageIndex < 1 ? 1 : q.PageIndex;
            var pageSize = q.PageSize < 1 ? 20 : (q.PageSize > 100 ? 100 : q.PageSize);

            var (total, items) = await _repository.QueryAsync(
                q.DeviceId,
                q.Level,
                q.Unacked,
                q.Unrecovered,
                q.StartTime,
                q.EndTime,
                pageIndex,
                pageSize);

            return new AlarmRecordPagedResultDto
            {
                Total = total,
                Items = items.Select(ToDto).ToList()
            };
        }

        /// <inheritdoc/>
        public async Task<AlarmRecordDto?> GetByIdAsync(long id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity == null ? null : ToDto(entity);
        }

        /// <inheritdoc/>
        public async Task<List<AlarmRecordDto>> GetActiveAsync()
        {
            // 当前未恢复（仍处于报警态）的记录，按触发时间倒序，供实时列表初始化。
            var items = await _repository.GetListAsync(r => r.RecoveredAt == null);
            return items.OrderByDescending(r => r.TriggeredAt).Select(ToDto).ToList();
        }

        /// <inheritdoc/>
        public async Task<bool> AckAsync(long id, string ackBy)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null || entity.Acked)
            {
                return false; // 不存在或已确认
            }

            entity.Acked = true;
            entity.AckedAt = DateTime.UtcNow;
            entity.AckedBy = string.IsNullOrWhiteSpace(ackBy) ? "unknown" : ackBy;
            await _repository.UpdateAsync(entity);
            return true;
        }

        /// <summary>将报警记录实体映射为 DTO。MySQL datetime 读回为 <c>Kind=Unspecified</c>，需显式标记为 UTC 保证 JSON 序列化带 Z 后缀。</summary>
        private static AlarmRecordDto ToDto(AlarmRecord e) => new()
        {
            Id = e.Id,
            DeviceId = e.DeviceId,
            DeviceKey = e.DeviceKey,
            VariableKey = e.VariableKey,
            VariableName = e.VariableName,
            RuleId = e.RuleId,
            RuleName = e.RuleName,
            Level = e.Level,
            Condition = e.Condition,
            Threshold = e.Threshold,
            ActualValue = e.ActualValue,
            Message = e.Message,
            Source = e.Source,
            // MySQL datetime 读回为 Kind=Unspecified，序列化不带 Z 后缀会被前端按本地时间解析。
            // 运行时已统一写入 UTC，此处显式标记 UTC 使 JSON 带 Z 后缀，前端 new Date() 正确本地化。
            // 注意：切换前入库的历史记录为本地时间，展示会有时区偏移。
            TriggeredAt = DateTime.SpecifyKind(e.TriggeredAt, DateTimeKind.Utc),
            RecoveredAt = e.RecoveredAt.HasValue ? DateTime.SpecifyKind(e.RecoveredAt.Value, DateTimeKind.Utc) : null,
            RecoveryValue = e.RecoveryValue,
            Acked = e.Acked,
            AckedAt = e.AckedAt.HasValue ? DateTime.SpecifyKind(e.AckedAt.Value, DateTimeKind.Utc) : null,
            AckedBy = e.AckedBy
        };
    }
}