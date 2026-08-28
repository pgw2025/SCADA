using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class SystemScriptAppService : ISystemScriptAppService
    {
        private readonly ISystemScriptRepository _repository;
        public SystemScriptAppService(ISystemScriptRepository repository) { _repository = repository; }

        private static SystemScriptDto ToDto(SystemScript e) => new()
        {
            Id = e.Id,
            Name = e.Name,
            Code = e.Code,
            TriggerType = e.TriggerType,
            IntervalSeconds = e.IntervalSeconds,
            CronExpression = e.CronExpression,
            WatchDeviceKey = e.WatchDeviceKey,
            WatchVariableKey = e.WatchVariableKey,
            DeadBand = e.DeadBand,
            CooldownMs = e.CooldownMs,
            TimeoutMs = e.TimeoutMs,
            ScopeRead = e.ScopeRead,
            ScopeWrite = e.ScopeWrite,
            Active = e.Active,
            Version = e.Version,
            FailureCount = e.FailureCount,
            Tripped = e.Tripped,
            LastError = e.LastError,
            LastExecutedAt = e.LastExecutedAt,
            LastDurationMs = e.LastDurationMs
        };

        public async Task<SystemScriptDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity == null ? null : ToDto(entity);
        }

        public async Task<List<SystemScriptDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(ToDto).ToList();
        }

        public async Task CreateAsync(SystemScriptDto dto)
        {
            var entity = new SystemScript
            {
                Name = dto.Name,
                Code = dto.Code,
                TriggerType = dto.TriggerType,
                IntervalSeconds = dto.IntervalSeconds,
                CronExpression = dto.CronExpression,
                WatchDeviceKey = dto.WatchDeviceKey,
                WatchVariableKey = dto.WatchVariableKey,
                DeadBand = dto.DeadBand,
                CooldownMs = dto.CooldownMs,
                TimeoutMs = dto.TimeoutMs,
                ScopeRead = dto.ScopeRead,
                ScopeWrite = dto.ScopeWrite,
                Active = dto.Active,
                Version = 1
            };
            await _repository.InsertAsync(entity);
            dto.Id = entity.Id;
        }

        public async Task UpdateAsync(SystemScriptDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return;

            entity.Name = dto.Name;
            entity.Code = dto.Code;
            entity.TriggerType = dto.TriggerType;
            entity.IntervalSeconds = dto.IntervalSeconds;
            entity.CronExpression = dto.CronExpression;
            entity.WatchDeviceKey = dto.WatchDeviceKey;
            entity.WatchVariableKey = dto.WatchVariableKey;
            entity.DeadBand = dto.DeadBand;
            entity.CooldownMs = dto.CooldownMs;
            entity.TimeoutMs = dto.TimeoutMs;
            entity.ScopeRead = dto.ScopeRead;
            entity.ScopeWrite = dto.ScopeWrite;
            entity.Active = dto.Active;
            // 手动编辑代码/元数据视为新一次发布：版本 +1，并复位熔断状态（代码已变更，重新评估）。
            entity.Version += 1;
            entity.FailureCount = 0;
            entity.Tripped = false;
            entity.LastError = null;
            await _repository.UpdateAsync(entity);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

        /// <inheritdoc/>
        public async Task ResetTrippedAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            entity.Tripped = false;
            entity.FailureCount = 0;
            entity.LastError = null;
            await _repository.UpdateAsync(entity);
        }
    }
}