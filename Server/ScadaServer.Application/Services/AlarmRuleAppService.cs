using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 报警规则应用服务实现：负责报警规则的增删改查（CRUD）。
    /// 规则仅做持久化，实际报警判定由运行时引擎根据规则实时评估。
    /// </summary>
    public class AlarmRuleAppService : IAlarmRuleAppService
    {
        /// <summary>报警规则仓储，提供持久化能力。</summary>
        private readonly IAlarmRuleRepository _repository;

        /// <summary>构造函数：注入报警规则仓储。</summary>
        public AlarmRuleAppService(IAlarmRuleRepository repository)
        {
            _repository = repository;
        }

        /// <summary>按主键获取报警规则，不存在时返回 null。</summary>
        public async Task<AlarmRuleDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        /// <summary>获取全部报警规则列表。</summary>
        public async Task<List<AlarmRuleDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        /// <summary>新增报警规则，并将生成的主键写回 DTO。</summary>
        public async Task CreateAsync(AlarmRuleDto dto)
        {
            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            dto.Id = entity.Id;
        }

        /// <summary>按 DTO 携带的主键更新既有规则；记录不存在时直接返回。</summary>
        public async Task UpdateAsync(AlarmRuleDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return;
            entity.Name = dto.Name;
            entity.DeviceId = dto.DeviceId;
            entity.VariableKey = dto.VariableKey;
            entity.Condition = dto.Condition;
            entity.Threshold = dto.Threshold;
            entity.Level = dto.Level;
            entity.Active = dto.Active;
            entity.Message = dto.Message;
            entity.DebounceSeconds = dto.DebounceSeconds;
            await _repository.UpdateAsync(entity);
        }

        /// <summary>删除指定报警规则；记录不存在时静默忽略。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

        /// <summary>将报警规则实体映射为 DTO。</summary>
        private static AlarmRuleDto MapToDto(AlarmRule entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            DeviceId = entity.DeviceId,
            VariableKey = entity.VariableKey,
            Condition = entity.Condition,
            Threshold = entity.Threshold,
            Level = entity.Level,
            Active = entity.Active,
            Message = entity.Message,
            DebounceSeconds = entity.DebounceSeconds
        };

        /// <summary>将 DTO 映射为报警规则实体。</summary>
        private static AlarmRule MapToEntity(AlarmRuleDto dto) => new()
        {
            Name = dto.Name,
            DeviceId = dto.DeviceId,
            VariableKey = dto.VariableKey,
            Condition = dto.Condition,
            Threshold = dto.Threshold,
            Level = dto.Level,
            Active = dto.Active,
            Message = dto.Message,
            DebounceSeconds = dto.DebounceSeconds
        };
    }
}
