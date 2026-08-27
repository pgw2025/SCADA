using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    public class AlarmRuleAppService : IAlarmRuleAppService
    {
        private readonly IAlarmRuleRepository _repository;

        public AlarmRuleAppService(IAlarmRuleRepository repository)
        {
            _repository = repository;
        }

        public async Task<AlarmRuleDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        public async Task<List<AlarmRuleDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task CreateAsync(AlarmRuleDto dto)
        {
            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            dto.Id = entity.Id;
        }

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

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

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
