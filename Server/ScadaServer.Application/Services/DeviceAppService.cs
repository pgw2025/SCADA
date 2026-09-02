using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Enums;
using System.Text.Json;
using ScadaServer.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

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

        /// <summary>
        /// 协议配置反序列化选项：属性名大小写不敏感，兼容 camelCase / PascalCase 两种存储格式。
        /// 与驱动侧（如 <c>S7Driver.ConfigJsonOptions</c>）保持一致，避免存储格式差异导致派生字段读不到值。
        /// </summary>
        private static readonly JsonSerializerOptions ConfigReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>将设备实体映射为 DTO，并解析运行时状态与连接参数派生字段。</summary>
        private DeviceDto ToDto(Device entity, List<DeviceVariableDto>? variables)
        {
            var dto = new DeviceDto
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

            // 真相源是 JsonConfig，以下仅为按协议投影出的只读连接字段
            ApplyConnectionFields(dto, entity.Model?.Protocol?.DriverKey, entity.JsonConfig);
            return dto;
        }

        /// <summary>
        /// 由协议配置 JSON 派生连接参数并写入 DTO 的只读字段。
        /// <para>
        /// 真相源为 <c>Device.JsonConfig</c>：按 <c>Protocol.DriverKey</c> 路由到对应配置类，
        /// 反序列化后投影。不做任何回写，<c>JsonConfig</c> 仍是唯一可写入口。
        /// </para>
        /// <para>
        /// 解析失败（配置缺失、JSON 非法、协议未识别）一律静默返回、字段保持 null，
        /// 由前端显示"未配置"。单个设备的坏配置不应拖垮整个设备列表查询。
        /// </para>
        /// </summary>
        private static void ApplyConnectionFields(DeviceDto dto, string? driverKey, string? configJson)
        {
            if (string.IsNullOrWhiteSpace(configJson)) return;

            try
            {
                switch (ResolveDriverKind(driverKey))
                {
                    case DriverKind.S7:
                    {
                        var c = JsonSerializer.Deserialize<S7Config>(configJson!, ConfigReadOptions);
                        if (c == null) return;
                        dto.IpAddress = c.IpAddress;
                        dto.Port = c.Port;
                        dto.CpuType = c.CpuType;
                        dto.Rack = c.Rack;
                        dto.Slot = c.Slot;
                        return;
                    }
                    case DriverKind.ModbusTcp:
                    {
                        var c = JsonSerializer.Deserialize<ModbusTcpConfig>(configJson!, ConfigReadOptions);
                        if (c == null) return;
                        dto.IpAddress = c.IpAddress;
                        dto.Port = c.Port;
                        dto.UnitId = c.UnitId;
                        return;
                    }
                    case DriverKind.OpcUa:
                    {
                        var c = JsonSerializer.Deserialize<OpcUaConfig>(configJson!, ConfigReadOptions);
                        if (c == null) return;
                        // 端点地址原样返回，前端回填/展示必须用整串，用 ip+port 拼接会丢路径
                        dto.EndpointUrl = c.EndpointUrl;
                        dto.IpAddress = TryExtractUriHost(c.EndpointUrl);
                        dto.Port = TryExtractUriPort(c.EndpointUrl);
                        return;
                    }
                    case DriverKind.Mqtt:
                    {
                        var c = JsonSerializer.Deserialize<MqttConfig>(configJson!, ConfigReadOptions);
                        if (c == null) return;
                        dto.Broker = c.Broker;
                        dto.Port = c.Port;
                        dto.Topic = c.Topic;
                        return;
                    }
                    case DriverKind.Virtual:
                    {
                        var c = JsonSerializer.Deserialize<VirtualConfig>(configJson!, ConfigReadOptions);
                        if (c == null) return;
                        dto.IntervalMs = c.IntervalMs;
                        dto.RandomValues = c.RandomValues;
                        return;
                    }
                }
            }
            catch (JsonException)
            {
                // 配置非法：字段保持 null，前端显示"未配置"
            }
        }

        /// <summary>从 OPC UA 端点地址解析主机名；非法或不含主机信息返回 null。</summary>
        private static string? TryExtractUriHost(string? endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl)) return null;
            return Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
        }

        /// <summary>
        /// 从 OPC UA 端点地址解析端口号；未显式指定端口或地址非法返回 null。
        /// （非标准 scheme 未指定端口时 <c>Uri.Port</c> 为 -1，此处归一为 null。）
        /// </summary>
        private static int? TryExtractUriPort(string? endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl)) return null;
            if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri)) return null;
            return uri.Port > 0 ? uri.Port : null;
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
                ScaleExpressionOverride = dv.ScaleExpressionOverride,
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
        /// 校验设备名称唯一：Trim + 大小写不敏感 + 忽略尾随空格（与 MySQL ci 排序规则语义一致）。
        /// 返回规范化后的名称供入库，保证存储与去重口径一致。
        /// </summary>
        private async Task<(bool ok, string normName)> ResolveUniqueNameAsync(string rawName, int? excludeId)
        {
            var name = rawName?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                throw new BusinessException("设备名称不能为空");
            }

            // 直接按名称比较：MySQL 默认 ci 排序规则大小写不敏感、忽略尾随空格，
            // 无需 ToLowerInvariant（EF 无法翻译该方法到 SQL）。
            var peers = await _repository.GetListAsync(d =>
                d.Id != (excludeId ?? 0) && d.Name == name);
            if (peers.Any())
            {
                throw new BusinessException($"设备名称 '{name}' 已存在");
            }

            return (true, name);
        }

        /// <summary>
        /// 校验协议端点唯一（同一驱动内）。S7 校验 IpAddress+Port，OPC UA 校验 EndpointUrl。
        /// 仅当配置解析成功且地址非空时才参与去重；Modbus/MQTT/Virtual 不参与。
        /// 兼容纯驱动键（S7/OPCUA）与驱动类名（S7Driver/OPCUADriver）。
        /// </summary>
        private async Task EnsureEndpointUniqueAsync(string? driverKey, string configJson, int? excludeId)
        {
            if (string.IsNullOrWhiteSpace(configJson)) return;   // 更新语义：空=保留原配置

            var probeKey = ParseEndpointKey(driverKey, configJson);
            if (probeKey == null) return;                         // 非 S7/OPC UA 或地址为空，跳过

            var all = await _repository.GetListAsync();           // 带导航（Area/Model.Protocol），AsNoTracking
            foreach (var d in all)
            {
                if (d.Id == excludeId) continue;
                var dk = d.Model?.Protocol?.DriverKey;
                if (dk == null) continue;

                var peerKey = ParseEndpointKey(dk, d.JsonConfig ?? string.Empty);
                if (peerKey != null
                    && string.Equals(peerKey, probeKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessException(
                        IsS7Driver(driverKey ?? string.Empty)
                            ? $"S7 设备的 IP+端口 '{probeKey}' 已被设备 '{d.Name}' 使用"
                            : $"OPC UA 服务器地址 '{probeKey}' 已被设备 '{d.Name}' 使用");
                }
            }
        }

        /// <summary>
        /// 从协议配置 JSON 提取端点唯一键；非 S7/OPC UA 或地址为空返回 null。
        /// </summary>
        private static string? ParseEndpointKey(string? driverKey, string configJson)
        {
            if (string.IsNullOrEmpty(configJson)) return null;
            var dk = driverKey?.Trim() ?? string.Empty;

            bool isS7 = IsS7Driver(dk);
            bool isOpc = IsOpcUaDriver(dk);
            if (!isS7 && !isOpc) return null;

            try
            {
                using var doc = JsonDocument.Parse(configJson);
                var root = doc.RootElement;

                if (isS7)
                {
                    var ip = TryGetStringProperty(root, "IpAddress");
                    if (string.IsNullOrWhiteSpace(ip)) return null;
                    var portEl = TryGetJsonProperty(root, "Port");
                    var port = portEl.HasValue
                        && portEl.Value.ValueKind == JsonValueKind.Number
                        && portEl.Value.TryGetInt32(out var p)
                        ? p
                        : 102;
                    return $"{ip!.Trim().ToLowerInvariant()}|{port}";
                }

                var url = TryGetStringProperty(root, "EndpointUrl");
                if (string.IsNullOrWhiteSpace(url)) return null;
                return url!.Trim().TrimEnd('/').ToLowerInvariant();
            }
            catch (JsonException)
            {
                // 单台设备的配置 JSON 非法时跳过该设备的去重比对，
                // 不让一台坏设备导致任意 S7/OPC UA 设备的创建/更新直接 500。
                return null;
            }
        }

        /// <summary>
        /// 在 JSON 对象中按名称查找属性（大小写不敏感，与派生字段读取口径一致）；未找到返回 null。
        /// </summary>
        private static JsonElement? TryGetJsonProperty(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
            }
            return null;
        }

        /// <summary>读取 JSON 对象中的字符串属性；属性缺失或非字符串类型时返回 null。</summary>
        private static string? TryGetStringProperty(JsonElement root, string name)
        {
            var element = TryGetJsonProperty(root, name);
            return element.HasValue && element.Value.ValueKind == JsonValueKind.String
                ? element.Value.GetString()
                : null;
        }

        /// <summary>判断驱动键是否为 S7（兼容 S7 / S7Driver 写法）。</summary>
        private static bool IsS7Driver(string dk)
            => string.Equals(dk, "S7", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dk, "S7Driver", StringComparison.OrdinalIgnoreCase);

        /// <summary>判断驱动键是否为 OPC UA（兼容 OPCUA / OPCUADriver 写法）。</summary>
        private static bool IsOpcUaDriver(string dk)
            => string.Equals(dk, "OPCUA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dk, "OPCUADriver", StringComparison.OrdinalIgnoreCase);

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
                => DbExceptionClassifier.IsUniqueIndexConflict(ex);

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

            // 3.5 名称唯一 + S7/OPC UA 端点唯一（新建设备无 Id，排除项传 null）
            dto.Name = (await ResolveUniqueNameAsync(dto.Name, null)).normName;
            await EnsureEndpointUniqueAsync(driverKey, dto.ConfigJson, null);

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

            // 3.5 名称唯一（排除自身）+ S7/OPC UA 端点唯一（排除自身）
            //     更新语义：ConfigJson 为空表示保留原配置，跳过端点去重。
            dto.Name = (await ResolveUniqueNameAsync(dto.Name, dto.Id)).normName;
            if (!string.IsNullOrEmpty(dto.ConfigJson))
            {
                await EnsureEndpointUniqueAsync(model.Protocol?.DriverKey!, dto.ConfigJson, dto.Id);
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
        /// 启用/停用设备的采集并同步运行时（启用→注册、停用→注销）。设备不存在时抛 BusinessException；
        /// 启用状态无变化时幂等返回，不触发运行时注册/注销。
        /// </summary>
        public async Task<DeviceDto> SetEnabledAsync(int id, bool enabled)
        {
            var entity = await _repository.GetByIdForUpdateAsync(id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {id} 的设备不存在");
            }

            // 启用状态无变化：直接返回当前设备，不触发运行时注册/注销（天然幂等）。
            if (entity.IsEnabled == enabled)
            {
                return await GetByIdAsync(id)
                    ?? throw new BusinessException($"ID 为 {id} 的设备不存在");
            }

            entity.IsEnabled = enabled;
            entity.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(entity);

            // 状态变化提交成功后，与运行时交互：启用 → 注册；停用 → 注销（断开驱动、推送 Offline）。
            if (enabled)
            {
                await _runtimeDeviceManager.RegisterDeviceAsync(id);
            }
            else
            {
                await _runtimeDeviceManager.RemoveDeviceAsync(id);
            }

            return await GetByIdAsync(id)
                ?? throw new BusinessException($"ID 为 {id} 的设备不存在");
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
