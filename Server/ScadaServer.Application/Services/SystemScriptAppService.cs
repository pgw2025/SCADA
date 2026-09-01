using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 系统脚本应用服务实现：负责用户编写的系统脚本（触发式/定时式/变化式）的增删改查。
    /// 编辑脚本视为一次发布：版本 +1 并复位熔断状态；也支持手动复位脚本熔断。
    /// </summary>
    public class SystemScriptAppService : ISystemScriptAppService
    {
        /// <summary>系统脚本仓储，提供持久化能力。</summary>
        private readonly ISystemScriptRepository _repository;

        /// <summary>构造函数：注入系统脚本仓储。</summary>
        public SystemScriptAppService(ISystemScriptRepository repository) { _repository = repository; }

        /// <summary>将系统脚本实体映射为 DTO。</summary>
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

        /// <summary>按主键获取脚本，不存在时返回 null。</summary>
        public async Task<SystemScriptDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity == null ? null : ToDto(entity);
        }

        /// <summary>获取全部脚本列表。</summary>
        public async Task<List<SystemScriptDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(ToDto).ToList();
        }

        /// <summary>新增脚本（初始版本为 1），并将生成的主键写回 DTO。</summary>
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

        /// <summary>更新脚本：代码/元数据变更视为一次发布（版本 +1）并复位熔断状态；记录不存在时直接返回。</summary>
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

        /// <summary>删除脚本；记录不存在时静默忽略。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

        /// <summary>手动复位脚本熔断状态（清除熔断标志、失败计数与错误信息）。</summary>
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