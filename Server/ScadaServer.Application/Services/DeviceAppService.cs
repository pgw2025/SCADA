using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Enums;
using System.Text.Json;
using ScadaServer.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 设备应用服务实现：负责设备的查询、创建、更新、删除以及向设备运行时写入变量值。
    /// 创建/更新成功后按启用状态变化同步注册/注销/重载设备运行时实例，
    /// 并在事务内维护协议配置与设备变量实例。
    /// </summary>
    public class DeviceAppService : IDeviceAppService
    {
        /// <summary>设备仓储，提供设备实体持久化能力。</summary>
        private readonly IDeviceRepository _repository;
        /// <summary>区域仓储，用于校验区域是否存在。</summary>
        private readonly IAreaRepository _areaRepository;
        /// <summary>数据模型仓储，用于校验模型是否存在并推导协议。</summary>
        private readonly IDataModelRepository _modelRepository;
        /// <summary>变量模板仓储，用于生成设备变量实例。</summary>
        private readonly IModelVariableRepository _modelVariableRepository;
        /// <summary>设备变量实例仓储，用于聚合设备变量。</summary>
        private readonly IDeviceVariableRepository _deviceVariableRepository;
        /// <summary>工作单元，提供事务能力。</summary>
        private readonly IUnitOfWork _uow;
        /// <summary>运行时状态供应，用于解析设备在线状态。</summary>
        private readonly IRuntimeStatusProvider _runtimeStatusProvider;
        /// <summary>删除服务，负责设备删除前的依赖检查与级联清理。</summary>
        private readonly IDeviceDeletionService _deletionService;
        /// <summary>运行时设备管理器，用于设备注册/注销/重载。</summary>
        private readonly IRuntimeDeviceManager _runtimeDeviceManager;

        /// <summary>构造函数：注入设备及其关联仓储、事务单元、运行时状态与设备管理器。</summary>
        public DeviceAppService(
            IDeviceRepository repository,
            IAreaRepository areaRepository,
            IDataModelRepository modelRepository,
            IModelVariableRepository modelVariableRepository,
            IDeviceVariableRepository deviceVariableRepository,
            IUnitOfWork uow,
            IRuntimeStatusProvider runtimeStatusProvider,
            IDeviceDeletionService deletionService,
            IRuntimeDeviceManager runtimeDeviceManager)
        {
            _repository = repository;
            _areaRepository = areaRepository;
            _modelRepository = modelRepository;
            _modelVariableRepository = modelVariableRepository;
            _deviceVariableRepository = deviceVariableRepository;
            _uow = uow;
            _runtimeStatusProvider = runtimeStatusProvider;
            _deletionService = deletionService;
            _runtimeDeviceManager = runtimeDeviceManager;
        }

        /// <summary>按主键获取设备及其关联变量，不存在时返回 null。</summary>
        public async Task<DeviceDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;

            var variables = await LoadDeviceVariablesAsync(entity.Id, entity.ModelId);
            return ToDto(entity, variables);
        }

        /// <summary>获取设备列表；includeVariables 为 true 时按设备聚合其变量定义（N+1 优化）。</summary>
        public async Task<List<DeviceDto>> GetListAsync(bool includeVariables = true)
        {
            var list = await _repository.GetListAsync();

            // N+1 优化：一次性加载全量设备变量与变量模板，循环内仅内存组装，避免每台设备额外查询。
            Dictionary<int, List<DeviceVariableDto>>? variablesByDevice = null;
            if (includeVariables && list.Count > 0)
            {
                var allDeviceVariables = await _deviceVariableRepository.GetListAsync();
                var allModelVariables = await _modelVariableRepository.GetListAsync();
                var mvMap = allModelVariables.ToDictionary(mv => mv.Id);

                variablesByDevice = allDeviceVariables
                    .GroupBy(dv => dv.DeviceId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(dv => MapDeviceVariableDto(dv, mvMap)).ToList());
            }

            return list.Select(entity =>
            {
                List<DeviceVariableDto> variables = variablesByDevice != null && variablesByDevice.TryGetValue(entity.Id, out var vs)
                    ? vs
                    : new List<DeviceVariableDto>();
                return ToDto(entity, variables);
            }).ToList();
        }

        /// <summary>将设备实体映射为 DTO，并解析运行时状态。</summary>
        private DeviceDto ToDto(Device entity, List<DeviceVariableDto>? variables)
        {
            return new DeviceDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Key = entity.Key,
                AreaId = entity.AreaId,
                ModelId = entity.ModelId,
                ProtocolKey = entity.Model?.Protocol?.Key,
                ProtocolName = entity.Model?.Protocol?.Name,
                IsEnabled = entity.IsEnabled,
                PollingInterval = entity.PollingInterval,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                LastCommunicationTime = entity.LastCommunicationTime,
                ConfigJson = entity.JsonConfig,
                RuntimeStatus = ResolveRuntimeStatus(entity.Id, entity.IsEnabled, entity.LastKnownStatus),
                Variables = variables
            };
        }

        /// <summary>将设备变量实例与其变量模板映射为 DTO（模板缺失时 Key/Name 为空、DataType 取默认值）。</summary>
        private static DeviceVariableDto MapDeviceVariableDto(DeviceVariable dv, Dictionary<int, ModelVariable> mvMap)
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
                AddressConfigJson = dv.AddressConfigJson,
                BitOffset = dv.BitOffset,
                PollingIntervalMs = dv.PollingIntervalMs,
                IsEnabled = dv.IsEnabled,
                ScaleSlopeOverride = dv.ScaleSlopeOverride,
                ScaleOffsetOverride = dv.ScaleOffsetOverride,
                DeadBandOverride = dv.DeadBandOverride
            };
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

            return deviceVariables.Select(dv => MapDeviceVariableDto(dv, mvMap)).ToList();
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
        /// 解析校验用的驱动种类。采用协议真相源 <paramref name="driverKey"/>（来自 Protocol.DriverKey），
        /// 兼容 <c>S7Driver</c>/<c>S7</c> 等写法；协议必填后不再有过渡字段回退。
        /// </summary>
        private static DriverKind ResolveDriverKind(string? driverKey)
        {
            switch (driverKey?.Trim().ToUpperInvariant())
            {
                case "S7" or "S7DRIVER": return DriverKind.S7;
                case "MODBUSTCP" or "MODBUSTCPDRIVER": return DriverKind.ModbusTcp;
                case "OPCUA" or "OPCUADRIVER": return DriverKind.OpcUa;
                case "MQTT" or "MQTTDRIVER": return DriverKind.Mqtt;
                case "VIRTUAL" or "VIRTUALDRIVER": return DriverKind.Virtual;
                default: return DriverKind.Unknown;
            }
        }

        /// <summary>
        /// 验证协议配置 JSON 格式（按协议驱动键路由到对应的配置类）。
        /// </summary>
        private void ValidateConfigJson(string? driverKey, string configJson)
        {
            try
            {
                switch (ResolveDriverKind(driverKey))
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

        /// <summary>
        /// 判断 EF 保存异常是否为 MySQL 唯一键冲突（错误码 1062，如设备标识唯一索引）。
        /// </summary>
        private static bool IsUniqueIndexConflict(DbUpdateException ex)
            => ex.GetBaseException() is MySqlException mySql
                && (mySql.ErrorCode == MySqlErrorCode.DuplicateKeyEntry || mySql.Number == 1062);

        /// <summary>
        /// 创建设备：校验区域/模型/协议驱动与配置 JSON 格式，生成或校验设备标识，
        /// 在事务内创建设备、协议配置并依据变量模板自动生成设备变量实例；
        /// 事务提交成功后，将已启用的设备注册进运行时以便立即开始采集。
        /// </summary>
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
            // 协议真相源为所绑定数据模型的 Protocol.DriverKey（模型必绑协议后不再回退过渡字段）。
            var driverKey = model.Protocol?.DriverKey;
            if (!ProtocolDriverSupport.IsDriverImplemented(driverKey))
            {
                throw new BusinessException($"协议 {driverKey ?? "(未绑定)"} 的驱动尚未实现，暂不支持创建设备。当前可用协议：S7、OPC UA、Virtual。");
            }

            // 2. 验证协议配置 JSON 格式（按协议驱动键对应的配置类校验）
            ValidateConfigJson(driverKey, dto.ConfigJson);

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

            var created = await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                var entity = new Device
                {
                    Name = dto.Name,
                    Key = dto.Key!,
                    AreaId = dto.AreaId,
                    ModelId = dto.ModelId,
                    IsEnabled = dto.IsEnabled,
                    PollingInterval = dto.PollingInterval,
                    JsonConfig = string.IsNullOrEmpty(dto.ConfigJson) ? "{}" : dto.ConfigJson,
                    Version = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                try
                {
                    await _repository.InsertAsync(entity);
                }
                catch (DbUpdateException ex) when (IsUniqueIndexConflict(ex))
                {
                    // 并发竞态兜底：预检通过但落库时撞设备标识唯一索引
                    throw new BusinessException($"设备标识 '{dto.Key}' 已存在");
                }

                // 根据数据模型的变量模板，自动生成设备变量实例（DeviceVariable）。
                // 地址/位偏移/轮询间隔等采集细节已迁移到设备实例层，此处仅创建实例（IsEnabled=true），
                // 具体地址后续在设备变量接口上单独配置；模板层不再携带这些字段。
                var modelVariables = await _modelVariableRepository.GetListAsync(mv => mv.ModelId == model.Id);
                if (modelVariables.Any())
                {
                    var deviceVariables = modelVariables.Select(mv => new DeviceVariable
                    {
                        DeviceId = entity.Id,
                        ModelVariableId = mv.Id,
                        IsEnabled = true
                    }).ToList();
                    await _deviceVariableRepository.InsertRangeAsync(deviceVariables);
                }

                return await GetByIdAsync(entity.Id)
                    ?? throw new BusinessException($"创建设备后无法读取 ID 为 {entity.Id} 的设备记录");
            });

            // 事务提交成功后，将已启用的新设备注册进运行时，使其无需重启即可开始采集。
            if (created.IsEnabled)
            {
                await _runtimeDeviceManager.RegisterDeviceAsync(created.Id);
            }

            return created;
        }

        /// <summary>
        /// 更新设备：禁止变更设备绑定的数据模型，校验设备标识唯一性与区域/模型存在，
        /// 事务内更新设备属性与协议配置（ConfigJson 为空时保留原配置）；
        /// 提交成功后按启用状态变化执行运行时注册/注销/重载（热加载）。
        /// </summary>
        public async Task<DeviceDto> UpdateAsync(DeviceDto dto)
        {
            var entity = await _repository.GetByIdForUpdateAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的设备不存在");
            }
            // 记录更新前的启用状态，供事务提交后判断运行时注册/注销/重载。
            var wasEnabled = entity.IsEnabled;

            // 1. 结构性约束：设备所绑定的数据模型不可变更。
            //    更换模型会令既有设备变量实例的模板引用失配、协议随之变化，必须删除后按新模型重建。
            if (dto.ModelId != entity.ModelId)
            {
                throw new BusinessException("不支持变更设备绑定的数据模型，请删除设备后重新创建。");
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

            // 3. 验证协议配置 JSON 格式（协议随所绑定模型推导）
            //    注：PUT 语义中 ConfigJson 为空表示"不修改协议配置"（保留原值），仅在前端回传时校验收录。
            if (!string.IsNullOrEmpty(dto.ConfigJson))
            {
                ValidateConfigJson(model.Protocol?.DriverKey, dto.ConfigJson);
            }

            var updated = await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                entity.Name = dto.Name;
                entity.Key = dto.Key;
                entity.AreaId = dto.AreaId;
                entity.ModelId = dto.ModelId;
                entity.IsEnabled = dto.IsEnabled;
                entity.PollingInterval = dto.PollingInterval;
                entity.UpdatedAt = DateTime.UtcNow;

                await _repository.UpdateAsync(entity);

                // 更新协议配置（ConfigJson 为空时保留旧配置，非全量清空语义）。
                // 配置字段已内联到 Device（原 DeviceConfig），版本号随配置更新自增。
                if (!string.IsNullOrEmpty(dto.ConfigJson))
                {
                    entity.JsonConfig = dto.ConfigJson;
                    entity.Version++;
                }

                return await GetByIdAsync(dto.Id)
                    ?? throw new BusinessException($"更新设备后无法读取 ID 为 {dto.Id} 的设备记录");
            });

            // 事务提交成功后，依据启用状态变化与运行时交互：
            // 禁用 → 注销；启用（原本禁用）→ 注册；保持启用 → 重载（热加载配置/模型/变量变更）。
            if (updated.IsEnabled)
            {
                if (wasEnabled)
                {
                    await _runtimeDeviceManager.ReloadDeviceAsync(updated.Id);
                }
                else
                {
                    await _runtimeDeviceManager.RegisterDeviceAsync(updated.Id);
                }
            }
            else if (wasEnabled)
            {
                await _runtimeDeviceManager.RemoveDeviceAsync(updated.Id);
            }

            return updated;
        }

        /// <summary>删除设备：依赖检查与级联清理委托给删除服务，完成后注销设备运行时实例。</summary>
        public async Task DeleteAsync(int id)
        {
            // 依赖检查与级联清理逻辑已抽离至 IDeviceDeletionService（对外接口/传感器/触发器/配置）
            await _deletionService.DeleteAsync(id);

            // 级联删除完成后，若设备仍在运行时则注销（移除 Worker、断开驱动、推送 Offline）。
            await _runtimeDeviceManager.RemoveDeviceAsync(id);
        }

        /// <summary>
        /// 向设备运行时变量写入值。运行时校验及物理写入失败原因经 IRuntimeDeviceManager 返回，
        /// 失败时抛 BusinessException 由全局异常处理转成 { success=false, message } 供前端展示。
        /// </summary>
        public async Task WriteVariableAsync(int deviceId, string variableKey, object value)
        {
            var (success, errorMessage) = await _runtimeDeviceManager.WriteVariableAsync(deviceId, variableKey, value);
            if (!success)
            {
                throw new BusinessException(errorMessage ?? "变量写入失败");
            }
        }
    }
}
