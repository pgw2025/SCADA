using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    public class MqttVariableConfigAppService : IMqttVariableConfigAppService
    {
        private readonly IRepository<MqttVariableConfig, int> _repository;
        private readonly IMqttServerRepository _serverRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IDeviceVariableRepository _deviceVariableRepository;
        private readonly IModelVariableRepository _modelVariableRepository;
        private readonly IVariableRealtimeRepository _realtimeRepository;
        private readonly IMqttManager _mqttManager;

        public MqttVariableConfigAppService(
            IRepository<MqttVariableConfig, int> repository,
            IMqttServerRepository serverRepository,
            IDeviceRepository deviceRepository,
            IDeviceVariableRepository deviceVariableRepository,
            IModelVariableRepository modelVariableRepository,
            IVariableRealtimeRepository realtimeRepository,
            IMqttManager mqttManager)
        {
            _repository = repository;
            _serverRepository = serverRepository;
            _deviceRepository = deviceRepository;
            _deviceVariableRepository = deviceVariableRepository;
            _modelVariableRepository = modelVariableRepository;
            _realtimeRepository = realtimeRepository;
            _mqttManager = mqttManager;
        }

        public async Task<List<MqttVariableConfigDto>> GetByServerAsync(int serverId)
        {
            var mappings = await _repository.GetListAsync(m => m.MqttServerId == serverId);
            if (mappings.Count == 0) return new List<MqttVariableConfigDto>();

            var server = await _serverRepository.GetByIdAsync(serverId);
            var prefix = server?.TopicPrefix?.TrimEnd('/') ?? "scada";

            var deviceIds = mappings.Select(m => m.DeviceId).Distinct().ToList();
            var deviceNames = (await _deviceRepository.GetListAsync(d => deviceIds.Contains(d.Id)))
                .ToDictionary(d => d.Id, d => d.Name);

            // 变量名映射：(DeviceId, ModelVariable.Key) -> ModelVariable.Name
            var deviceVariables = await _deviceVariableRepository.GetListAsync(dv => deviceIds.Contains(dv.DeviceId));
            var modelVariableIds = deviceVariables.Select(dv => dv.ModelVariableId).Distinct().ToList();
            var modelVariables = modelVariableIds.Count == 0
                ? new List<ModelVariable>()
                : await _modelVariableRepository.GetListAsync(mv => modelVariableIds.Contains(mv.Id));
            var modelById = modelVariables.ToDictionary(mv => mv.Id, mv => mv);
            var varNameLookup = new Dictionary<(int, string), string>();
            foreach (var dv in deviceVariables)
            {
                if (modelById.TryGetValue(dv.ModelVariableId, out var mv))
                {
                    varNameLookup[(dv.DeviceId, mv.Key)] = mv.Name;
                }
            }

            var result = new List<MqttVariableConfigDto>();
            foreach (var m in mappings)
            {
                var realtime = await _realtimeRepository.GetByDeviceAndKeyAsync(m.DeviceId, m.VariableKey);
                result.Add(new MqttVariableConfigDto
                {
                    Id = m.Id,
                    MqttServerId = m.MqttServerId,
                    DeviceId = m.DeviceId,
                    DeviceName = deviceNames.GetValueOrDefault(m.DeviceId) ?? string.Empty,
                    VariableKey = m.VariableKey,
                    VariableName = varNameLookup.GetValueOrDefault((m.DeviceId, m.VariableKey))
                        ?? realtime?.VariableName ?? string.Empty,
                    Alias = m.Alias,
                    CustomTopic = m.CustomTopic,
                    IsEnabled = m.IsEnabled,
                    TopicPreview = BuildTopicPreview(m, prefix),
                    RealtimeValue = realtime == null ? null : (realtime.RawValue ?? realtime.Value.ToString())
                });
            }

            return result;
        }

        public async Task<MqttVariableConfigDto> AddAsync(int serverId, MqttVariableConfigCreateDto dto)
        {
            var exists = await _repository.AnyAsync(m =>
                m.MqttServerId == serverId && m.DeviceId == dto.DeviceId && m.VariableKey == dto.VariableKey);
            if (exists)
            {
                throw new BusinessException("该服务器下已关联此变量，请勿重复关联。");
            }

            var entity = new MqttVariableConfig
            {
                MqttServerId = serverId,
                DeviceId = dto.DeviceId,
                VariableKey = dto.VariableKey.Trim(),
                Alias = dto.Alias.Trim(),
                CustomTopic = string.IsNullOrWhiteSpace(dto.CustomTopic) ? null : dto.CustomTopic.Trim(),
                IsEnabled = true
            };
            await _repository.InsertAsync(entity);
            await _mqttManager.ReloadAsync();

            return (await GetByServerAsync(serverId)).First(m => m.Id == entity.Id);
        }

        public async Task<MqttVariableConfigDto?> UpdateAsync(int configId, MqttVariableConfigUpdateDto dto)
        {
            var entity = await _repository.GetByIdAsync(configId);
            if (entity == null) return null;

            entity.Alias = dto.Alias.Trim();
            entity.CustomTopic = string.IsNullOrWhiteSpace(dto.CustomTopic) ? null : dto.CustomTopic.Trim();
            entity.IsEnabled = dto.IsEnabled;
            await _repository.UpdateAsync(entity);
            await _mqttManager.ReloadAsync();

            return (await GetByServerAsync(entity.MqttServerId)).FirstOrDefault(m => m.Id == configId);
        }

        public async Task DeleteAsync(int configId)
        {
            var entity = await _repository.GetByIdAsync(configId);
            if (entity == null) return;

            await _repository.DeleteAsync(entity);
            await _mqttManager.ReloadAsync();
        }

        /// <summary>
        /// 计算完整推送主题（与 MqttManager 发布逻辑一致）：自定义主题优先，否则「前缀/别名」。
        /// </summary>
        private static string BuildTopicPreview(MqttVariableConfig m, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(m.CustomTopic)) return m.CustomTopic.Trim();
            return string.IsNullOrEmpty(prefix) ? m.Alias : $"{prefix}/{m.Alias}";
        }
    }
}