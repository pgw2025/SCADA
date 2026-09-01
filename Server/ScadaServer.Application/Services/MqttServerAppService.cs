using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// MQTT 服务器（桥接/订阅源）应用服务：负责 MQTT 服务器配置的增删改查与启停。
    /// 维护服务器下变量映射数；配置变更后同步刷新 MQTT 管理器运行时；
    /// 删除服务器时级联清理其变量映射。密码不回传明文。
    /// </summary>
    public class MqttServerAppService : IMqttServerAppService
    {
        /// <summary>MQTT 服务器仓储，提供持久化能力。</summary>
        private readonly IMqttServerRepository _repository;
        /// <summary>变量映射仓储，用于统计映射数及级联清理。</summary>
        private readonly IRepository<MqttVariableConfig, int> _mappingRepository;
        /// <summary>MQTT 管理器，配置变更后热重载运行时。</summary>
        private readonly IMqttManager _mqttManager;

        /// <summary>构造函数：注入 MQTT 服务器、映射仓储及管理器。</summary>
        public MqttServerAppService(
            IMqttServerRepository repository,
            IRepository<MqttVariableConfig, int> mappingRepository,
            IMqttManager mqttManager)
        {
            _repository = repository;
            _mappingRepository = mappingRepository;
            _mqttManager = mqttManager;
        }

        /// <summary>按主键获取 MQTT 服务器（含变量映射数），不存在时返回 null。</summary>
        public async Task<MqttServerDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;

            var dto = MapToDto(entity);
            dto.VariableCount = await _mappingRepository.CountAsync(m => m.MqttServerId == id);
            return dto;
        }

        /// <summary>获取全部 MQTT 服务器列表；含映射数，启用状态优先、按 Id 排序。</summary>
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

        /// <summary>新增 MQTT 服务器，写回生成的主键并刷新运行时。</summary>
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

        /// <summary>更新 MQTT 服务器（密码留空保持原值）并刷新运行时；记录不存在时静默忽略。</summary>
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

        /// <summary>启用/停用指定 MQTT 服务器并刷新运行时；记录不存在时静默忽略。</summary>
        public async Task SetEnabledAsync(int id, bool enabled)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            entity.IsEnabled = enabled;
            await _repository.UpdateAsync(entity);
            await _mqttManager.ReloadAsync();
        }

        /// <summary>删除 MQTT 服务器：级联清理其变量映射后删除并刷新运行时；记录不存在时静默忽略。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            // 级联清理该服务器下的变量映射，避免残留孤儿映射。
            await _mappingRepository.DeleteRangeAsync(m => m.MqttServerId == id);
            await _repository.DeleteAsync(entity);
            await _mqttManager.ReloadAsync();
        }

        /// <summary>获取全部 MQTT 服务器的运行时连接状态。</summary>
        public Task<List<MqttServerStatusDto>> GetStatusesAsync() => _mqttManager.GetStatusesAsync();

        /// <summary>测试到指定 MQTT Broker 的连接是否可用（委托给 MQTT 管理器）。</summary>
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