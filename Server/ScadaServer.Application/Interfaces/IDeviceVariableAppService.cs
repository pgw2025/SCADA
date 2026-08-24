using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces;

/// <summary>
/// 设备变量应用服务接口：聚合变量模板定义与设备实例配置，支持变量级地址 / 启用 / 轮询维护。
/// 本服务操作的是"变量模板在某台具体设备上的实例"（DeviceVariable），与 ModelVariable（模板）相互独立。
/// </summary>
public interface IDeviceVariableAppService
{
    /// <summary>获取某设备下的全部设备变量（聚合模板定义与实例配置）。</summary>
    Task<List<DeviceVariableDto>> GetByDeviceAsync(int deviceId);

    /// <summary>
    /// 创建设备变量实例（按"设备 + 变量模板"），地址 / 位偏移 / 采集周期默认从模板回退。
    /// 用于在模板新增变量后，为已存在设备补齐对应的变量实例。
    /// </summary>
    Task<DeviceVariableDto> CreateAsync(CreateDeviceVariableDto dto);

    /// <summary>删除某个设备变量实例。</summary>
    Task DeleteAsync(int id);

    /// <summary>
    /// 更新某个设备变量的实例配置：支持修改变量地址、位偏移、采集周期（轮询间隔）、启用/禁用，以及缩放/死区覆盖。
    /// </summary>
    Task<DeviceVariableDto> UpdateAsync(DeviceVariableDto dto);
}
