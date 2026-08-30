using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    public class MqttServerAppService : IMqttServerAppService
    {
        private readonly IMqttServerRepository _repository;
        private readonly IRepository<MqttVariableConfig, int> _mappingRepository;
        private readonly IMqttManager _mqttManager;

        public MqttServerAppService(
            IMqttServerRepository repository,
            IRepository<MqttVariableConfig, int> mappingRepository,
            IMqttManager mqttManager)
        {
            _repository = repository;
            _mappingRepository = mappingRepository;
            _mqttManager = mqttManager;
        }

        public async Task<MqttServerDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;

            var dto = MapToDto(entity);
            dto.VariableCount = await _mappingRepository.CountAsync(m => m.MqttServerId == id);
            return dto;
        }

        public async Task<List<MqttServerDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            // 一次查出所有映射数，按服务器分组填充（避免每条记录一次 Count 查询）。
            var counts = (await _mappingRepository.GetListAsync())
                .GroupBy(m => m.MqttServerId)
                .ToDictionary(g => g.Key, g => g.Count());

            return list
                .Select(entity =>
                {
                    var dto = MapToDto(entity);
                    dto.VariableCount = counts.GetValueOrDefault(entity.Id);
                    return dto;
                })
                .OrderByDescending(d => d.IsEnabled)
                .ThenBy(d => d.Id)
                .ToList();
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
                Password = dto.Password ?? string.Empty,
                TopicPrefix = dto.TopicPrefix,
                IsEnabled = dto.IsEnabled
            };
            await _repository.InsertAsync(entity);
            dto.Id = entity.Id;
            await _mqttManager.ReloadAsync();
        }

        public async Task UpdateAsync(MqttServerDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return;

            entity.Name = dto.Name;
            entity.BrokerUrl = dto.BrokerUrl;
            entity.Port = dto.Port;
            entity.ClientId = dto.ClientId;
            entity.Username = dto.Username;
            entity.TopicPrefix = dto.TopicPrefix;
            entity.IsEnabled = dto.IsEnabled;

            // 密码：仅当客户端显式提供了非空密码时才更新（空值=保持原密码）。
            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                entity.Password = dto.Password;
            }

            await _repository.UpdateAsync(entity);
            await _mqttManager.ReloadAsync();
        }

        public async Task SetEnabledAsync(int id, bool enabled)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            entity.IsEnabled = enabled;
            await _repository.UpdateAsync(entity);
            await _mqttManager.ReloadAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            // 级联清理该服务器下的变量映射，避免残留孤儿映射。
            await _mappingRepository.DeleteRangeAsync(m => m.MqttServerId == id);
            await _repository.DeleteAsync(entity);
            await _mqttManager.ReloadAsync();
        }

        public Task<List<MqttServerStatusDto>> GetStatusesAsync() => _mqttManager.GetStatusesAsync();

        public Task<MqttTestConnectionResultDto> TestConnectionAsync(MqttTestConnectionDto dto) =>
            _mqttManager.TestConnectionAsync(dto);

        /// <summary>
        /// 实体 -> DTO（密码不回传明文）。
        /// </summary>
        private static MqttServerDto MapToDto(MqttServer entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            BrokerUrl = entity.BrokerUrl,
            Port = entity.Port,
            ClientId = entity.ClientId,
            Username = entity.Username,
            Password = string.Empty,
            TopicPrefix = entity.TopicPrefix,
            IsEnabled = entity.IsEnabled
        };
    }
}