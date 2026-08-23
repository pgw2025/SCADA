using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class SystemConfigAppService : ISystemConfigAppService
    {
        private readonly ISystemConfigRepository _repository;
        public SystemConfigAppService(ISystemConfigRepository repository) { _repository = repository; }

        public async Task<SystemConfigDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new SystemConfigDto
            {
                Id = entity.Id,
                SystemTitle = entity.SystemTitle,
                PollIntervalMs = entity.PollIntervalMs,
                MqttBrokerHost = entity.MqttBrokerHost,
                RetentionPeriodDays = entity.RetentionPeriodDays
            };
        }

        public async Task<List<SystemConfigDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new SystemConfigDto
            {
                Id = entity.Id,
                SystemTitle = entity.SystemTitle,
                PollIntervalMs = entity.PollIntervalMs,
                MqttBrokerHost = entity.MqttBrokerHost,
                RetentionPeriodDays = entity.RetentionPeriodDays
            }).ToList();
        }

        public async Task CreateAsync(SystemConfigDto dto)
        {
            var entity = new SystemConfig
            {
                SystemTitle = dto.SystemTitle,
                PollIntervalMs = dto.PollIntervalMs,
                MqttBrokerHost = dto.MqttBrokerHost,
                RetentionPeriodDays = dto.RetentionPeriodDays
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(SystemConfigDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.SystemTitle = dto.SystemTitle;
                entity.PollIntervalMs = dto.PollIntervalMs;
                entity.MqttBrokerHost = dto.MqttBrokerHost;
                entity.RetentionPeriodDays = dto.RetentionPeriodDays;
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

