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
        private readonly IModelVariableRepository _modelVariableRepository;
        private readonly IDeviceVariableRepository _deviceVariableRepository;
        private readonly IRepository<DeviceConfig, int> _configRepository;
        private readonly IUnitOfWork _uow;
        private readonly IRuntimeStatusProvider _runtimeStatusProvider;

        public DeviceAppService(
            IDeviceRepository repository,
            ISensorRepository sensorRepository,
            IVariableTriggerRepository triggerRepository,

            IExposedInterfaceRepository interfaceRepository,
            IAreaRepository areaRepository,
            IDataModelRepository modelRepository,
            IModelVariableRepository modelVariableRepository,
            IDeviceVariableRepository deviceVariableRepository,
            IRepository<DeviceConfig, int> configRepository,
            IUnitOfWork uow,
            IRuntimeStatusProvider runtimeStatusProvider)
        {
            _repository = repository;
            _sensorRepository = sensorRepository;
            _triggerRepository = triggerRepository;
            _interfaceRepository = interfaceRepository;

            _areaRepository = areaRepository;
            _modelRepository = modelRepository;
            _modelVariableRepository = modelVariableRepository;
            _deviceVariableRepository = deviceVariableRepository;
            _configRepository = configRepository;
            _uow = uow;
            _runtimeStatusProvider = runtimeStatusProvider;
        }

        public async Task<DeviceDto?> GetByIdAsync(int id)
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
                ModelType = entity.Model?.Type ?? default,
                ProtocolKey = entity.Model?.Protocol?.Key,
                ProtocolName = entity.Model?.Protocol?.Name,
                IsEnabled = entity.IsEnabled,
                PollingInterval = entity.PollingInterval,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                LastCommunicationTime = entity.LastCommunicationTime,
                ConfigJson = entity.Config?.JsonConfig,
                RuntimeStatus = ResolveRuntimeStatus(entity.Id, entity.IsEnabled, entity.LastKnownStatus),
                Variables = await LoadDeviceVariablesAsync(entity.Id, entity.ModelId)
            };
        }

        public async Task<List<DeviceDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            var dtos = new List<DeviceDto>();
            foreach (var entity in list)
            {
                dtos.Add(new DeviceDto
                {
                    Id = entity.Id,
                    Name = entity.Name,
                    Key = entity.Key,
                    AreaId = entity.AreaId,
                    ModelId = entity.ModelId,
                    ModelType = entity.Model?.Type ?? default,
                    ProtocolKey = entity.Model?.Protocol?.Key,
                    ProtocolName = entity.Model?.Protocol?.Name,
                    IsEnabled = entity.IsEnabled,
                    PollingInterval = entity.PollingInterval,
                    CreatedAt = entity.CreatedAt,
                    UpdatedAt = entity.UpdatedAt,
                    LastCommunicationTime = entity.LastCommunicationTime,
                    ConfigJson = entity.Config?.JsonConfig,
                    RuntimeStatus = ResolveRuntimeStatus(entity.Id, entity.IsEnabled, entity.LastKnownStatus),
                    Variables = await LoadDeviceVariablesAsync(entity.Id, entity.ModelId)
                });
            }
            return dtos;
        }

        /// <summary>
        /// 聚合某设备的设备变量：关联各自的变量模板（ModelVariable），输出"定义 + 设备实例配置"。
        /// </summary>
        private async Task<List<DeviceVariableDto>> LoadDeviceVariablesAsync(int deviceId, int modelId)
        {
            var deviceVariables = await _deviceVariableRepository.GetListAsync(dv => dv.DeviceId == deviceId);
            if (deviceVariables.Count == 0)
            {
                return new List<DeviceVariableDto>();
            }

            var modelVariables = await _modelVariableRepository.GetListAsync(mv => mv.ModelId == modelId);
            var mvMap = modelVariables.ToDictionary(mv => mv.Id);

            return deviceVariables.Select(dv =>
            {
                mvMap.TryGetValue(dv.ModelVariableId, out var mv);
                return new DeviceVariableDto
                {
                    Id = dv.Id,
                    DeviceId = dv.DeviceId,
                    ModelVariableId = dv.ModelVariableId,
                    Key = mv?.Key ?? string.Empty,
                    Name = mv?.Name ?? string.Empty,
                    DataType = mv?.DataType ?? default,
                    Unit = mv?.Unit,
                    Address = dv.Address,
                    BitOffset = dv.BitOffset,
                    PollingIntervalMs = dv.PollingIntervalMs,
                    IsEnabled = dv.IsEnabled,
                    ScaleSlopeOverride = dv.ScaleSlopeOverride,
                    ScaleOffsetOverride = dv.ScaleOffsetOverride,
                    DeadBandOverride = dv.DeadBandOverride
                };
            }).ToList();
        }

        /// <summary>
        /// 解析设备运行时状态。优先级：
        /// 1) 设备已禁用 → Offline；
        /// 2) 运行时内存态（实时）→ 直接采用；
        /// 3) 运行时未加载（重启瞬间 / 初始化失败）→ 优先采用数据库持久化的 LastKnownStatus，
        ///    使重启后仍能展示设备最后已知状态，而非一律 Offline；
        /// 4) 既无内存态也无持久态 → Offline。
        /// </summary>
        private DeviceStatus ResolveRuntimeStatus(int deviceId, bool isEnabled, DeviceStatus? lastKnownStatus)
        {
            if (!isEnabled)
            {
                return DeviceStatus.Offline;
            }

            if (_runtimeStatusProvider.TryGetRuntimeStatus(deviceId, out var status))
            {
                return status;
            }

            return lastKnownStatus ?? DeviceStatus.Offline;
        }

        /// <summary>
        /// 内部驱动种类，用于协议配置校验路由。
        /// </summary>
        private enum DriverKind { S7, ModbusTcp, OpcUa, Mqtt, Virtual, Unknown }

        /// <summary>
        /// 解析校验用的驱动种类。优先采用协议真相源 <paramref name="driverKey"/>（来自 Protocol.DriverKey），
        /// 兼容 <c>S7Driver</c>/<c>S7</c> 等写法；为空时回退到过渡字段 <paramref name="type"/>。
        /// </summary>
        private static DriverKind ResolveDriverKind(DeviceType type, string? driverKey)
        {
            if (!string.IsNullOrWhiteSpace(driverKey))
            {
                switch (driverKey.Trim().ToUpperInvariant())
                {
                    case "S7" or "S7DRIVER": return DriverKind.S7;
                    case "MODBUSTCP" or "MODBUSTCPDRIVER": return DriverKind.ModbusTcp;
                    case "OPCUA" or "OPCUADRIVER": return DriverKind.OpcUa;
                    case "MQTT" or "MQTTDRIVER": return DriverKind.Mqtt;
                    case "VIRTUAL" or "VIRTUALDRIVER": return DriverKind.Virtual;
                }
            }
            return type switch
            {
                DeviceType.S7 => DriverKind.S7,
                DeviceType.ModbusTcp => DriverKind.ModbusTcp,
                DeviceType.OpcUa => DriverKind.OpcUa,
                DeviceType.Mqtt => DriverKind.Mqtt,
                DeviceType.Virtual => DriverKind.Virtual,
                _ => DriverKind.Unknown
            };
        }

        /// <summary>
        /// 验证协议配置 JSON 格式（按协议驱动键路由到对应的配置类）。
        /// </summary>
        private void ValidateConfigJson(DeviceType type, string? driverKey, string configJson)
        {
            try
            {
                switch (ResolveDriverKind(type, driverKey))
                {
                    case DriverKind.S7:
                        JsonSerializer.Deserialize<S7Config>(configJson);
                        break;
                    case DriverKind.ModbusTcp:
                        JsonSerializer.Deserialize<ModbusTcpConfig>(configJson);
                        break;
                    case DriverKind.OpcUa:
                        JsonSerializer.Deserialize<OpcUaConfig>(configJson);
                        break;
                    case DriverKind.Mqtt:
                        JsonSerializer.Deserialize<MqttConfig>(configJson);
                        break;
                    case DriverKind.Virtual:
                        JsonSerializer.Deserialize<VirtualConfig>(configJson);
                        break;
                    default:
                        // 未知协议，仅验证是否为有效 JSON
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
        /// 按区域编码自动生成设备标识：{AreaCode}-{序号:000}。
        /// 区域未配置 Code 时回退为 A{AreaId}。
        /// </summary>
        private async Task<string> GenerateDeviceCodeAsync(Area area)
        {
            var baseCode = !string.IsNullOrWhiteSpace(area.Code) ? area.Code! : $"A{area.Id}";
            var prefix = baseCode + "-";
            var siblings = await _repository.GetListAsync(d => d.Key.StartsWith(prefix));
            var maxSeq = 0;
            foreach (var s in siblings)
            {
                var parts = s.Key.Split('-');
                if (parts.Length >= 2 && int.TryParse(parts[^1], out var n))
                {
                    maxSeq = Math.Max(maxSeq, n);
                }
            }
            return $"{baseCode}-{maxSeq + 1:000}";
        }

        /// <summary>
        /// 为自动生成的设备标识确保全局唯一：若冲突则重新取最大序号+1 重试。
        /// 数据库唯一索引为最终保障。
        /// </summary>
        private async Task<string> EnsureUniqueGeneratedKeyAsync(string candidate, Area area)
        {
            var key = candidate;
            for (var i = 0; i < 50; i++)
            {
                var existing = await _repository.GetListAsync(d => d.Key == key);
                if (!existing.Any())
                {
                    return key;
                }
                key = await GenerateDeviceCodeAsync(area);
            }
            throw new BusinessException("生成设备标识失败：唯一键冲突过多，请手动指定标识或稍后重试。");
        }

        public async Task<DeviceDto> CreateAsync(CreateDeviceDto dto)
        {
            // 1. 存在性检查：校验区域和模型是否存在
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

            // 协议驱动前置校验：未实现驱动的协议在运行时初始化阶段才会失败，
            // 提前在此拦截并返回友好错误，避免设备被创建后无法进入运行时。
            // 协议真相源为所绑定数据模型的 Protocol.DriverKey（无协议时回退到过渡字段 model.Type）。
            var driverKey = model.Protocol?.DriverKey;
            var protocolImplemented =
                (driverKey != null && ProtocolDriverSupport.IsDriverImplemented(driverKey))
                || (driverKey == null && model.Type.IsDriverImplemented());
            if (!protocolImplemented)
            {
                throw new BusinessException($"协议 {driverKey ?? model.Type.ToString()} 的驱动尚未实现，暂不支持创建设备。当前可用协议：S7、OPC UA、Virtual。");
            }

            // 2. 验证协议配置 JSON 格式（按协议驱动键对应的配置类校验）
            ValidateConfigJson(model.Type, driverKey, dto.ConfigJson);

            // 4. 设备标识：未提供则由后台按区域自动生成（如 BLR-001），并确保全局唯一
            if (string.IsNullOrWhiteSpace(dto.Key))
            {
                dto.Key = await EnsureUniqueGeneratedKeyAsync(await GenerateDeviceCodeAsync(area), area);
            }
            else
            {
                var existing = await _repository.GetListAsync(d => d.Key == dto.Key);
                if (existing.Any())
                {
                    throw new BusinessException($"设备标识 '{dto.Key}' 已存在");
                }
            }

            return await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                var entity = new Device
                {
                    Name = dto.Name,
                    Key = dto.Key!,
                    AreaId = dto.AreaId,
                    ModelId = dto.ModelId,
                    IsEnabled = dto.IsEnabled,
                    PollingInterval = dto.PollingInterval,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await _repository.InsertAsync(entity);

                // 创建协议配置
                var config = new DeviceConfig
                {
                    DeviceId = entity.Id,
                    JsonConfig = string.IsNullOrEmpty(dto.ConfigJson) ? "{}" : dto.ConfigJson,
                    Version = 1,
                    UpdatedAt = DateTime.Now
                };
                await _configRepository.InsertAsync(config);

                // 根据数据模型的变量模板，自动生成设备变量实例（DeviceVariable）。
                // 初始值从模板复制地址/位偏移/轮询间隔；后续可在设备变量接口上单独覆盖。
                var modelVariables = await _modelVariableRepository.GetListAsync(mv => mv.ModelId == model.Id);
#pragma warning disable CS0618 // 过渡期：从模板读取已标记 [Obsolete] 的地址/位偏移/轮询字段
                if (modelVariables.Any())
                {
                    var deviceVariables = modelVariables.Select(mv => new DeviceVariable
                    {
                        DeviceId = entity.Id,
                        ModelVariableId = mv.Id,
                        Address = mv.Address,
                        BitOffset = mv.BitOffset,
                        PollingIntervalMs = mv.PollingIntervalMs,
                        IsEnabled = true
                    }).ToList();
                    await _deviceVariableRepository.InsertRangeAsync(deviceVariables);
                }
#pragma warning restore CS0618

                return await GetByIdAsync(entity.Id)
                    ?? throw new BusinessException($"创建设备后无法读取 ID 为 {entity.Id} 的设备记录");
            });
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

            // 3. 验证协议配置 JSON 格式（允许改绑定模型，协议随模型推导）
            if (!string.IsNullOrEmpty(dto.ConfigJson))
            {
                ValidateConfigJson(model.Type, model.Protocol?.DriverKey, dto.ConfigJson);
            }

            return await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                entity.Name = dto.Name;
                entity.Key = dto.Key;
                entity.AreaId = dto.AreaId;
                entity.ModelId = dto.ModelId;
                entity.IsEnabled = dto.IsEnabled;
                entity.PollingInterval = dto.PollingInterval;
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

                return await GetByIdAsync(dto.Id)
                    ?? throw new BusinessException($"更新设备后无法读取 ID 为 {dto.Id} 的设备记录");
            });
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

            await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                // 删除级联数据
                await _sensorRepository.DeleteRangeAsync(s => s.DeviceId == id);
                await _triggerRepository.DeleteRangeAsync(t => t.DeviceId == id);

                await _configRepository.DeleteRangeAsync(c => c.DeviceId == id);

                // 删除设备
                await _repository.DeleteAsync(entity);

                return true;
            });
        }
    }
}
