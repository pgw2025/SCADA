using Microsoft.EntityFrameworkCore;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Addresses;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services;

/// <summary>
/// 设备变量应用服务：负责设备变量（DataPointMapping）的查询与维护。
/// 设备变量描述"变量模板在某台具体设备上的实例"，聚合模板定义与实例级覆盖配置。
/// </summary>
public class DataPointMappingAppService : IDataPointMappingAppService
{
    /// <summary>设备变量仓储，提供持久化能力。</summary>
    private readonly IDataPointMappingRepository _repository;
    /// <summary>模型变量仓储，用于解析变量模板定义。</summary>
    private readonly IDataPointRepository _dataPointRepository;
    /// <summary>设备仓储，用于校验设备存在性及解析其绑定模型。</summary>
    private readonly IDeviceRepository _deviceRepository;
    /// <summary>设备-数据模型绑定仓储（阶段 5），用于取设备绑定模型集合（含主/附加）。</summary>
    private readonly IDeviceDataModelRepository _deviceDataModelRepository;
    /// <summary>系统脚本仓储，用于联动清理引用被删除变量的脚本。</summary>
    private readonly ISystemScriptRepository _systemScriptRepository;
    /// <summary>运行时设备管理器，用于增删改后热加载设备采集。</summary>
    private readonly IRuntimeDeviceManager _runtimeDeviceManager;
    /// <summary>工作单元，用于将"脚本联动清理 + 删除变量"包进同一事务，保证原子性。</summary>
    private readonly IUnitOfWork _uow;

    /// <summary>构造函数：注入设备变量、模型变量、设备、系统脚本仓储、运行时设备管理器及工作单元。</summary>
    public DataPointMappingAppService(
        IDataPointMappingRepository repository,
        IDataPointRepository dataPointRepository,
        IDeviceRepository deviceRepository,
        IDeviceDataModelRepository deviceDataModelRepository,
        ISystemScriptRepository systemScriptRepository,
        IRuntimeDeviceManager runtimeDeviceManager,
        IUnitOfWork uow)
    {
        _repository = repository;
        _dataPointRepository = dataPointRepository;
        _deviceRepository = deviceRepository;
        _deviceDataModelRepository = deviceDataModelRepository;
        _systemScriptRepository = systemScriptRepository;
        _runtimeDeviceManager = runtimeDeviceManager;
        _uow = uow;
    }

    /// <summary>获取指定设备下的全部设备变量（聚合其变量模板定义）。</summary>
    public async Task<List<DataPointMappingDto>> GetByDeviceAsync(int deviceId)
    {
        var device = await _deviceRepository.GetByIdAsync(deviceId);
        if (device == null)
        {
            throw new BusinessException($"ID 为 {deviceId} 的设备不存在");
        }

        var dataPointMappings = await _repository.GetListAsync(dv => dv.DeviceId == deviceId);
        // 模板范围 = 设备绑定模型集合（主模型 ∪ 附加绑定模型，Bug#2）：附加模型的映射在此也能取到完整模板信息。
        var boundModelIds = await GetBoundModelIdsAsync(device);
        var dataPoints = await _dataPointRepository.GetListAsync(mv => boundModelIds.Contains(mv.ModelId));
        var mvMap = dataPoints.ToDictionary(mv => mv.Id);

        return dataPointMappings.Select(dv =>
        {
            mvMap.TryGetValue(dv.DataPointId, out var mv);
            return MapToDto(dv, mv);
        }).ToList();
    }

    public async Task<DataPointMappingDto> CreateAsync(CreateDataPointMappingDto dto)
    {
        // 1. 设备存在性
        var device = await _deviceRepository.GetByIdAsync(dto.DeviceId);
        if (device == null)
        {
            throw new BusinessException($"ID 为 {dto.DeviceId} 的设备不存在");
        }

        // 2. 模板存在性，且必须隶属于该设备所绑定的数据模型（主模型 ∪ 附加绑定模型）
        var mv = await _dataPointRepository.GetByIdAsync(dto.DataPointId);
        if (mv == null)
        {
            throw new BusinessException($"ID 为 {dto.DataPointId} 的变量模板不存在");
        }
        var boundModelIds = await GetBoundModelIdsAsync(device);
        if (!boundModelIds.Contains(mv.ModelId))
        {
            throw new BusinessException($"变量模板 '{mv.Name}' 不属于设备 '{device.Name}' 所绑定的数据模型，无法实例化到该设备");
        }

        // 3. 唯一性：同一设备上不能重复实例化同一模板
        if (await _repository.AnyAsync(dv => dv.DeviceId == dto.DeviceId && dv.DataPointId == dto.DataPointId))
        {
            throw new BusinessException($"设备 '{device.Name}' 上已存在变量模板 '{mv.Name}' 的实例");
        }

        // 4. 实例化到设备（地址/位偏移/采集周期以设备实例级配置为准；模板层已不再携带这些字段）
        var entity = new DataPointMapping
        {
            DeviceId = dto.DeviceId,
            DataPointId = dto.DataPointId,
            IsEnabled = dto.IsEnabled,
            // 记录性字段：新建实例即按模板类型快照（与迁移回填语义一致，驱动仍以 DataTypeEnum 解释）
            RawDataType = mv.DataType.ToString(),
            ExtensionData = null
        };

        try
        {
            await _repository.InsertAsync(entity);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueIndexConflict(ex))
        {
            // 并发竞态兜底：预检通过但落库时撞 (DeviceId, DataPointId) 唯一索引
            throw new BusinessException($"设备 '{device.Name}' 上已存在变量模板 '{mv.Name}' 的实例");
        }
        // 设备变量集合变化需热加载设备运行时（重建 Worker 与变量集合）。
        await _runtimeDeviceManager.ReloadDeviceAsync(dto.DeviceId);
        return MapToDto(entity, mv);
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return;

        // 在单一事务内完成"联动清理脚本 + 删除变量"，保证原子性：
        // 中途任何一步失败则整体回滚，不留"脚本已停用但变量未删"等半状态。
        await _uow.ExecuteInTransactionAsync(async _ =>
        {
            // 联动：停用引用该设备变量的 OnChange 脚本，并从写授权中剔除该变量条目。
            await ScriptVariableCleanupHelper.CleanupScriptsByVariableAsync(entity, _deviceRepository, _dataPointRepository, _systemScriptRepository);
            await _repository.DeleteAsync(entity);
            return true;
        });

        // 事务提交成功后，设备变量集合变化需热加载设备运行时。
        await _runtimeDeviceManager.ReloadDeviceAsync(entity.DeviceId);
    }

    public async Task<DataPointMappingDto> UpdateAsync(DataPointMappingDto dto)
    {
        var entity = await _repository.GetByIdAsync(dto.Id);
        if (entity == null)
        {
            throw new BusinessException($"ID 为 {dto.Id} 的设备变量不存在");
        }

        // 仅更新设备实例级配置：地址（JSON 权威）+ 展示串、位偏移、轮询间隔、启用状态、缩放/死区覆盖。
        if (!string.IsNullOrWhiteSpace(dto.AddressConfigJson))
        {
            entity.AddressConfigJson = NormalizeAddressConfig(dto.AddressConfigJson, out var display);
            entity.Address = display; // 展示串由 JSON 权威生成
        }
        else
        {
            // 兼容旧客户端/旧数据：未回传 JSON 时沿用回传展示串，不清空地信息。
            entity.AddressConfigJson = null;
            entity.Address = dto.Address;
        }
        entity.BitOffset = dto.BitOffset;
        entity.PollingIntervalMs = dto.PollingIntervalMs;
        entity.IsEnabled = dto.IsEnabled;
        // 覆盖表达式校验（与模板同规则）：非法配置直接拒绝，避免脏表达式进入运行时采集循环。
        var scaleError = ScaleExpressionValidator.Validate(dto.ScaleExpressionOverride);
        if (scaleError != null)
        {
            throw new BusinessException($"设备变量换算表达式覆盖值非法：{scaleError}");
        }
        // 归一化：空白串存为 null。null 才是"继承模板"的语义——运行时用 `??` 回退模板，
        // 空串会被视为有效覆盖（恒等变变换），导致模板换算公式静默失效。
        entity.ScaleExpressionOverride =
            string.IsNullOrWhiteSpace(dto.ScaleExpressionOverride) ? null : dto.ScaleExpressionOverride.Trim();
        entity.DeadBandOverride = dto.DeadBandOverride;
        entity.AccessModeOverride = NormalizeAccessModeOverride(dto.AccessModeOverride);
        // 阶段 4 新增列透传：变量级连接覆盖 + 原始类型字符串（空串归一化为 null，保持"未配置"语义）
        entity.ConnectionId = dto.ConnectionId;
        entity.RawDataType = string.IsNullOrWhiteSpace(dto.RawDataType) ? null : dto.RawDataType.Trim();

        await _repository.UpdateAsync(entity);

        // 采集配置（地址/轮询/启用等）变化需热加载设备运行时。
        await _runtimeDeviceManager.ReloadDeviceAsync(entity.DeviceId);

        var mv = await _dataPointRepository.GetByIdAsync(entity.DataPointId);
        return MapToDto(entity, mv);
    }

    /// <summary>
    /// 取设备可实例化变量模板的模型集合 = 设备绑定模型（DeviceDataModels，含主/附加）
    /// ∪ <see cref="Device.ModelId"/> 兜底（兼容理论上绑定表缺主行的旧数据；正常路径绑定表含主行）。
    /// </summary>
    private async Task<List<int>> GetBoundModelIdsAsync(Device device)
    {
        var bindings = await _deviceDataModelRepository.GetByDeviceAsync(device.Id);
        var modelIds = bindings.Select(b => b.DataModelId).ToList();
        if (!modelIds.Contains(device.ModelId))
        {
            modelIds.Add(device.ModelId);
        }
        return modelIds;
    }

    /// <summary>
    /// 归一化实例级读写模式覆盖值：空白 → null（继承模板）；非空必须是 Read/Write/ReadWrite 之一，否则拒绝。
    /// </summary>
    private static string? NormalizeAccessModeOverride(string? overrideMode)
    {
        if (string.IsNullOrWhiteSpace(overrideMode)) return null;
        var mode = overrideMode.Trim();
        if (mode is not ("Read" or "Write" or "ReadWrite"))
        {
            throw new BusinessException($"设备变量读写模式覆盖值非法：'{overrideMode}'（可选 Read / Write / ReadWrite，留空=继承模板）");
        }
        return mode;
    }

    /// <summary>将设备变量实体与其模板映射为 DTO；模板缺失时以空串/默认值兜底。</summary>
    private static DataPointMappingDto MapToDto(DataPointMapping dv, DataPoint? mv)
    {
        var templateAccessMode = mv?.AccessMode ?? "Read";
        // 阶段 6 权限解析：实例覆盖（字符串 AccessModeOverride）优先，空则继承模板 AccessMode。
        var effectiveAccessMode = string.IsNullOrWhiteSpace(dv.AccessModeOverride)
            ? templateAccessMode
            : dv.AccessModeOverride!;
        return new DataPointMappingDto
        {
            Id = dv.Id,
            DeviceId = dv.DeviceId,
            DataPointId = dv.DataPointId,
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
            DeadBandOverride = dv.DeadBandOverride,
            AccessModeOverride = dv.AccessModeOverride,
            ConnectionId = dv.ConnectionId,
            RawDataType = dv.RawDataType,
            TemplateAccessMode = templateAccessMode,
            EffectiveAccessMode = effectiveAccessMode
        };
    }

    /// <summary>
    /// 归一化结构化地址：JSON 为唯一权威源。前端仅回传 <paramref name="addressConfigJson"/>，
    /// 后端解析并据此生成展示串（<c>Address</c>）。返回规范化后的 JSON，并输出展示串。
    /// <para>
    /// 兼容旧客户端：若未回传 JSON（空）则保持展示串不变（返回 null JSON、沿用原 Address）。
    /// </para>
    /// </summary>
    private static string? NormalizeAddressConfig(string? addressConfigJson, out string? display)
    {
        if (string.IsNullOrWhiteSpace(addressConfigJson))
        {
            display = null; // 未提供 JSON：由调用方决定是否保留旧展示串
            return null;
        }

        var config = AddressConfigSerializer.Deserialize(addressConfigJson)
            ?? throw new BusinessException("设备变量结构化地址（JSON）格式无效");

        // 按协议分类校验：只允许后端驱动支持结构化地址的协议。
        // 白名单外的协议（如 MQTT/BACnet/DNP3）不走 JSON 归属，维持纯文本 Address，
        // 若被传入 JSON 一律拒收，避免"JSON 落库但展示串为空"的脏状态。
        switch ((config.Protocol ?? "").Trim().ToUpperInvariant())
        {
            case "S7":
            case "OPCUA":
            case "MODBUS":
                // 必须能生成有效展示串；字段缺失/取值非法（ToDisplay 返回空）直接拒绝
                display = AddressConfigSerializer.ToDisplay(config);
                if (string.IsNullOrWhiteSpace(display))
                {
                    throw new BusinessException($"协议 '{config.Protocol}' 的结构化地址配置无效（缺少必要字段或取值非法）");
                }
                return AddressConfigSerializer.Serialize(config);

            case "VIRTUAL":
                // 虚拟设备无地址，合法放行：JSON 落库、展示串为空
                display = null;
                return AddressConfigSerializer.Serialize(config);

            default:
                throw new BusinessException($"协议 '{config.Protocol}' 不支持结构化地址配置（仅支持 S7 / OPC UA / Modbus）");
        }
    }
}
