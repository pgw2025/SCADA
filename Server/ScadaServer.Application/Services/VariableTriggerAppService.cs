using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class VariableTriggerAppService : IVariableTriggerAppService
    {
        private readonly IVariableTriggerRepository _repository;
        public VariableTriggerAppService(IVariableTriggerRepository repository) { _repository = repository; }

        public async Task<VariableTriggerDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new VariableTriggerDto
            {
                Id = entity.Id,
                Name = entity.Name,
                DeviceId = entity.DeviceId,
                VariableKey = entity.VariableKey,
                Condition = entity.Condition,
                Threshold = entity.Threshold,
                ActionType = entity.ActionType,
                AlarmLevel = entity.AlarmLevel,
                LinkageVariableKey = entity.LinkageVariableKey,
                LinkageValue = entity.LinkageValue,
                Active = entity.Active
            };
        }

        public async Task<List<VariableTriggerDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new VariableTriggerDto
            {
                Id = entity.Id,
                Name = entity.Name,
                DeviceId = entity.DeviceId,
                VariableKey = entity.VariableKey,
                Condition = entity.Condition,
                Threshold = entity.Threshold,
                ActionType = entity.ActionType,
                AlarmLevel = entity.AlarmLevel,
                LinkageVariableKey = entity.LinkageVariableKey,
                LinkageValue = entity.LinkageValue,
                Active = entity.Active
            }).ToList();
        }

        public async Task CreateAsync(VariableTriggerDto dto)
        {
            var entity = new VariableTrigger
            {
                Name = dto.Name,
                DeviceId = dto.DeviceId,
                VariableKey = dto.VariableKey,
                Condition = dto.Condition,
                Threshold = dto.Threshold,
                ActionType = dto.ActionType,
                AlarmLevel = dto.AlarmLevel,
                LinkageVariableKey = dto.LinkageVariableKey,
                LinkageValue = dto.LinkageValue,
                Active = dto.Active
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(VariableTriggerDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.DeviceId = dto.DeviceId;
                entity.VariableKey = dto.VariableKey;
                entity.Condition = dto.Condition;
                entity.Threshold = dto.Threshold;
                entity.ActionType = dto.ActionType;
                entity.AlarmLevel = dto.AlarmLevel;
                entity.LinkageVariableKey = dto.LinkageVariableKey;
                entity.LinkageValue = dto.LinkageValue;
                entity.Active = dto.Active;
                await _repository.UpdateAsync(entity);
            }
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }
    }
}

