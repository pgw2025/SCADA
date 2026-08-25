using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services;

/// <summary>
/// 设备变量应用服务：负责设备变量（DeviceVariable）的查询与维护。
/// 设备变量描述"变量模板在某台具体设备上的实例"，聚合模板定义与实例级覆盖配置。
/// </summary>
public class DeviceVariableAppService : IDeviceVariableAppService
{
    private readonly IDeviceVariableRepository _repository;
    private readonly IModelVariableRepository _modelVariableRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IRuntimeDeviceManager _runtimeDeviceManager;

    public DeviceVariableAppService(
        IDeviceVariableRepository repository,
        IModelVariableRepository modelVariableRepository,
        IDeviceRepository deviceRepository,
        IRuntimeDeviceManager runtimeDeviceManager)
    {
        _repository = repository;
        _modelVariableRepository = modelVariableRepository;
        _deviceRepository = deviceRepository;
        _runtimeDeviceManager = runtimeDeviceManager;
    }

    public async Task<List<DeviceVariableDto>> GetByDeviceAsync(int deviceId)
    {
        var device = await _deviceRepository.GetByIdAsync(deviceId);
        if (device == null)
        {
            throw new BusinessException($"ID 为 {deviceId} 的设备不存在");
        }

        var deviceVariables = await _repository.GetListAsync(dv => dv.DeviceId == deviceId);
        var modelVariables = await _modelVariableRepository.GetListAsync(mv => mv.ModelId == device.ModelId);
        var mvMap = modelVariables.ToDictionary(mv => mv.Id);

        return deviceVariables.Select(dv =>
        {
            mvMap.TryGetValue(dv.ModelVariableId, out var mv);
            return MapToDto(dv, mv);
        }).ToList();
    }

    public async Task<DeviceVariableDto> CreateAsync(CreateDeviceVariableDto dto)
    {
        // 1. 设备存在性
        var device = await _deviceRepository.GetByIdAsync(dto.DeviceId);
        if (device == null)
        {
            throw new BusinessException($"ID 为 {dto.DeviceId} 的设备不存在");
        }

        // 2. 模板存在性，且必须隶属于该设备所绑定的数据模型
        var mv = await _modelVariableRepository.GetByIdAsync(dto.ModelVariableId);
        if (mv == null)
        {
            throw new BusinessException($"ID 为 {dto.ModelVariableId} 的变量模板不存在");
        }
        if (mv.ModelId != device.ModelId)
        {
            throw new BusinessException($"变量模板 '{mv.Name}' 不属于设备 '{device.Name}' 所绑定的数据模型，无法实例化到该设备");
        }

        // 3. 唯一性：同一设备上不能重复实例化同一模板
        if (await _repository.AnyAsync(dv => dv.DeviceId == dto.DeviceId && dv.ModelVariableId == dto.ModelVariableId))
        {
            throw new BusinessException($"设备 '{device.Name}' 上已存在变量模板 '{mv.Name}' 的实例");
        }

        // 4. 实例化到设备（地址/位偏移/采集周期以设备实例级配置为准；模板层已不再携带这些字段）
        var entity = new DeviceVariable
        {
            DeviceId = dto.DeviceId,
            ModelVariableId = dto.ModelVariableId,
            IsEnabled = dto.IsEnabled,
            ExtensionData = null
        };

        await _repository.InsertAsync(entity);
        // 设备变量集合变化需热加载设备运行时（重建 Worker 与变量集合）。
        await _runtimeDeviceManager.ReloadDeviceAsync(dto.DeviceId);
        return MapToDto(entity, mv);
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity == null) return;

        await _repository.DeleteAsync(entity);

        // 设备变量集合变化需热加载设备运行时。
        await _runtimeDeviceManager.ReloadDeviceAsync(entity.DeviceId);
    }

    public async Task<DeviceVariableDto> UpdateAsync(DeviceVariableDto dto)
    {
        var entity = await _repository.GetByIdAsync(dto.Id);
        if (entity == null)
        {
            throw new BusinessException($"ID 为 {dto.Id} 的设备变量不存在");
        }

        // 仅更新设备实例级配置：地址、位偏移、轮询间隔、启用状态、缩放/死区覆盖。
        entity.Address = dto.Address;
        entity.BitOffset = dto.BitOffset;
        entity.PollingIntervalMs = dto.PollingIntervalMs;
        entity.IsEnabled = dto.IsEnabled;
        entity.ScaleSlopeOverride = dto.ScaleSlopeOverride;
        entity.ScaleOffsetOverride = dto.ScaleOffsetOverride;
        entity.DeadBandOverride = dto.DeadBandOverride;
        entity.IsReadOnlyOverride = dto.IsReadOnlyOverride;

        await _repository.UpdateAsync(entity);

        // 采集配置（地址/轮询/启用等）变化需热加载设备运行时。
        await _runtimeDeviceManager.ReloadDeviceAsync(entity.DeviceId);

        var mv = await _modelVariableRepository.GetByIdAsync(entity.ModelVariableId);
        return MapToDto(entity, mv);
    }

    private static DeviceVariableDto MapToDto(DeviceVariable dv, ModelVariable? mv) => new()
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
        DeadBandOverride = dv.DeadBandOverride,
        IsReadOnlyOverride = dv.IsReadOnlyOverride,
        TemplateIsReadOnly = mv?.IsReadOnly ?? true,
        EffectiveIsReadOnly = dv.IsReadOnlyOverride ?? (mv?.IsReadOnly ?? true)
    };
}
