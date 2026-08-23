using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class MqttServerAppService : IMqttServerAppService
    {
        private readonly IMqttServerRepository _repository;
        public MqttServerAppService(IMqttServerRepository repository) { _repository = repository; }

        public async Task<MqttServerDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new MqttServerDto
            {
                Id = entity.Id,
                Name = entity.Name,
                BrokerUrl = entity.BrokerUrl,
                Port = entity.Port,
                ClientId = entity.ClientId,
                Username = entity.Username,
                TopicPrefix = entity.TopicPrefix
            };
        }

        public async Task<List<MqttServerDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new MqttServerDto
            {
                Id = entity.Id,
                Name = entity.Name,
                BrokerUrl = entity.BrokerUrl,
                Port = entity.Port,
                ClientId = entity.ClientId,
                Username = entity.Username,
                TopicPrefix = entity.TopicPrefix
            }).ToList();
        }

        public async Task CreateAsync(MqttServerDto dto)
        {
            var entity = new MqttServer
            {
                Name = dto.Name,
                BrokerUrl = dto.BrokerUrl,
                Port = dto.Port,
                ClientId = dto.ClientId,
                Username = dto.Username,
                TopicPrefix = dto.TopicPrefix
            };
            // Note: Password is not in DTO for security reasons in this example
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(MqttServerDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                entity.Name = dto.Name;
                entity.BrokerUrl = dto.BrokerUrl;
                entity.Port = dto.Port;
                entity.ClientId = dto.ClientId;
                entity.Username = dto.Username;
                entity.TopicPrefix = dto.TopicPrefix;
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

