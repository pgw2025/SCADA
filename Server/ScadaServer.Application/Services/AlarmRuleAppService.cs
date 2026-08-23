using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class AlarmRuleAppService : IAlarmRuleAppService
    {
        private readonly IAlarmRuleRepository _repository;
        public AlarmRuleAppService(IAlarmRuleRepository repository) { _repository = repository; }

        public async Task<AlarmRuleDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new AlarmRuleDto
            {
                Id = entity.Id,
                SensorId = entity.SensorId,
                Condition = entity.Condition,
                Threshold = entity.Threshold,
                Severity = entity.Severity,
                IsEnabled = entity.IsEnabled
            };
        }

        public async Task<List<AlarmRuleDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new AlarmRuleDto
            {
                Id = entity.Id,
                SensorId = entity.SensorId,
                Condition = entity.Condition,
                Threshold = entity.Threshold,
                Severity = entity.Severity,
                IsEnabled = entity.IsEnabled
            }).ToList();
        }

        public async Task CreateAsync(AlarmRuleDto dto)
        {
            var entity = new AlarmRule
            {
                SensorId = dto.SensorId,
                Condition = dto.Condition,
                Threshold = dto.Threshold,
                Severity = dto.Severity,
                IsEnabled = dto.IsEnabled
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(AlarmRuleDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.SensorId = dto.SensorId;
                entity.Condition = dto.Condition;
                entity.Threshold = dto.Threshold;
                entity.Severity = dto.Severity;
                entity.IsEnabled = dto.IsEnabled;
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

