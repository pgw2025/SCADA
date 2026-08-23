using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Enums;
using System.Text.Json;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    public class DeviceAppService : IDeviceAppService
    {
        private readonly IDeviceRepository _repository;
        private readonly ISensorRepository _sensorRepository;
        private readonly IVariableTriggerRepository _triggerRepository;
        private readonly IExposedInterfaceRepository _interfaceRepository;
        private readonly IAreaRepository _areaRepository;
        private readonly IDataModelRepository _modelRepository;
        private readonly IRepository<DeviceConfig, int> _configRepository;
        private readonly IUnitOfWork _uow;

        public DeviceAppService(
            IDeviceRepository repository,
            ISensorRepository sensorRepository,
            IVariableTriggerRepository triggerRepository,

            IExposedInterfaceRepository interfaceRepository,
            IAreaRepository areaRepository,
            IDataModelRepository modelRepository,
            IRepository<DeviceConfig, int> configRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _sensorRepository = sensorRepository;
            _triggerRepository = triggerRepository;

            _areaRepository = areaRepository;
            _modelRepository = modelRepository;
            _configRepository = configRepository;
            _uow = uow;
        }

        public async Task<DeviceDto> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;

            return new DeviceDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Key = entity.Key,
                AreaId = entity.AreaId,
                ModelId = entity.ModelId,
                Type = entity.Type,
                IsEnabled = entity.IsEnabled,
                PollingInterval = entity.PollingInterval,
                DriverName = entity.DriverName,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                LastCommunicationTime = entity.LastCommunicationTime,
                ConfigJson = entity.Config?.JsonConfig
            };
        }

        public async Task<List<DeviceDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new DeviceDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Key = entity.Key,
                AreaId = entity.AreaId,
                ModelId = entity.ModelId,
                Type = entity.Type,
                IsEnabled = entity.IsEnabled,
                PollingInterval = entity.PollingInterval,
                DriverName = entity.DriverName,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                LastCommunicationTime = entity.LastCommunicationTime,
                ConfigJson = entity.Config?.JsonConfig
            }).ToList();
        }

        /// <summary>
        /// 验证协议配置 JSON 格式
        /// </summary>
        private void ValidateConfigJson(DeviceType type, string configJson)
        {
            try
            {
                switch (type)
                {
                    case DeviceType.S7:
                        JsonSerializer.Deserialize<S7Config>(configJson);
                        break;
                    case DeviceType.ModbusTcp:
                        JsonSerializer.Deserialize<ModbusTcpConfig>(configJson);
                        break;
                    case DeviceType.OpcUa:
                        JsonSerializer.Deserialize<OpcUaConfig>(configJson);
                        break;
                    case DeviceType.Mqtt:
                        JsonSerializer.Deserialize<MqttConfig>(configJson);
                        break;
                    case DeviceType.Virtual:
                        JsonSerializer.Deserialize<VirtualConfig>(configJson);
                        break;
                    default:
                        // 未知类型，仅验证是否为有效 JSON
                        JsonDocument.Parse(configJson);
                        break;
                }
            }
            catch (JsonException ex)
            {
                throw new BusinessException($"协议配置 JSON 格式无效: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取默认驱动名称
        /// </summary>
        private static string GetDefaultDriverName(DeviceType type) => type switch
        {
            DeviceType.S7 => "S7Driver",
            DeviceType.ModbusTcp => "ModbusTcpDriver",
            DeviceType.OpcUa => "OpcUaDriver",
            DeviceType.Mqtt => "MqttDriver",
            DeviceType.Virtual => "VirtualDriver",
            _ => $"{type}Driver"
        };

        public async Task<DeviceDto> CreateAsync(CreateDeviceDto dto)
        {
            // 1. 业务校验：Key 唯一性
            var existing = await _repository.GetListAsync(d => d.Key == dto.Key);
            if (existing.Any())
            {
                throw new BusinessException($"设备标识 '{dto.Key}' 已存在");
            }

            // 2. 存在性检查：校验区域和模型是否存在
            var area = await _areaRepository.GetByIdAsync(dto.AreaId);
            if (area == null)
            {
                throw new BusinessException($"ID 为 {dto.AreaId} 的区域不存在");
            }

            var model = await _modelRepository.GetByIdAsync(dto.ModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dto.ModelId} 的变量模型不存在");
            }

            // 3. 协议一致性检查：设备协议类型必须与模型定义的协议类型一致
            if (model.Type != dto.Type)
            {
                throw new BusinessException($"协议类型冲突。所选模型 '{model.Name}' 的类型为 {model.Type}，而当前设备配置的类型为 {dto.Type}。");
            }

            // 4. 验证协议配置 JSON 格式
            ValidateConfigJson(dto.Type, dto.ConfigJson);

            // 5. 设置默认驱动名称
            var driverName = string.IsNullOrEmpty(dto.DriverName)
                ? GetDefaultDriverName(dto.Type)
                : dto.DriverName;

            await using var transaction = await _uow.BeginTransactionAsync();
            try
            {
                var entity = new Device
                {
                    Name = dto.Name,
                    Key = dto.Key,
                    AreaId = dto.AreaId,
                    ModelId = dto.ModelId,
                    Type = dto.Type,
                    IsEnabled = dto.IsEnabled,
                    PollingInterval = dto.PollingInterval,
                    DriverName = driverName,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await _repository.InsertAsync(entity);

                // 创建协议配置
                var config = new DeviceConfig
                {
                    DeviceId = entity.Id,
                    JsonConfig = dto.ConfigJson,
                    Version = 1,
                    UpdatedAt = DateTime.Now
                };
                await _configRepository.InsertAsync(config);

                await transaction.CommitAsync();
                return await GetByIdAsync(entity.Id);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<DeviceDto> UpdateAsync(DeviceDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的设备不存在");
            }

            // 1. 业务校验：Key 不能与其他设备重复
            var existing = await _repository.GetListAsync(d => d.Key == dto.Key && d.Id != dto.Id);
            if (existing.Any())
            {
                throw new BusinessException($"设备标识 '{dto.Key}' 已存在");
            }

            // 2. 存在性检查：校验区域和模型是否存在
            var area = await _areaRepository.GetByIdAsync(dto.AreaId);
            if (area == null)
            {
                throw new BusinessException($"ID 为 {dto.AreaId} 的区域不存在");
            }

            var model = await _modelRepository.GetByIdAsync(dto.ModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dto.ModelId} 的变量模型不存在");
            }

            // 3. 协议一致性检查
            if (model.Type != dto.Type)
            {
                throw new BusinessException($"协议类型冲突。所选模型 '{model.Name}' 的类型为 {model.Type}，而当前设备配置的类型为 {dto.Type}。");
            }

            // 4. 验证协议配置 JSON 格式
            if (!string.IsNullOrEmpty(dto.ConfigJson))
            {
                ValidateConfigJson(dto.Type, dto.ConfigJson);
            }

            await using var transaction = await _uow.BeginTransactionAsync();
            try
            {
                entity.Name = dto.Name;
                entity.Key = dto.Key;
                entity.AreaId = dto.AreaId;
                entity.ModelId = dto.ModelId;
                entity.Type = dto.Type;
                entity.IsEnabled = dto.IsEnabled;
                entity.PollingInterval = dto.PollingInterval;
                entity.DriverName = dto.DriverName;
                entity.UpdatedAt = DateTime.Now;

                await _repository.UpdateAsync(entity);

                // 更新协议配置
                if (!string.IsNullOrEmpty(dto.ConfigJson) && entity.Config != null)
                {
                    entity.Config.JsonConfig = dto.ConfigJson;
                    entity.Config.Version++;
                    entity.Config.UpdatedAt = DateTime.Now;
                    await _configRepository.UpdateAsync(entity.Config);
                }

                await transaction.CommitAsync();
                return await GetByIdAsync(dto.Id);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            // 1. 依赖检查：检查是否被对外接口引用
            var interfaces = await _interfaceRepository.GetListAsync(i => i.DeviceId == id);
            if (interfaces.Any())
            {
                throw new BusinessException($"无法删除设备 '{entity.Name}'，因为它已被配置到 {interfaces.Count} 个对外数据接口中。请先解除绑定。");
            }

            await using var transaction = await _uow.BeginTransactionAsync();
            try
            {
                // 删除级联数据
                await _sensorRepository.DeleteRangeAsync(s => s.DeviceId == id);
                await _triggerRepository.DeleteRangeAsync(t => t.DeviceId == id);

                await _configRepository.DeleteRangeAsync(c => c.DeviceId == id);

                // 删除设备
                await _repository.DeleteAsync(entity);

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task UpdateDeviceConfigTxAsync(int deviceId, string newAddress) { }
    }
}
