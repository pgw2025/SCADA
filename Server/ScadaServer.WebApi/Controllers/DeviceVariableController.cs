using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers;

/// <summary>
/// 设备变量控制器：维护变量在设备上的实例配置（地址、位偏移、轮询间隔、启用状态、缩放/死区）。
/// 与 ModelVariable（变量模板）相互独立——本控制器操作的是"某设备上的某变量实例"。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DeviceVariableController : ControllerBase
{
    private readonly IDeviceVariableAppService _appService;

    public DeviceVariableController(IDeviceVariableAppService appService)
    {
        _appService = appService;
    }

    /// <summary>
    /// 获取某设备下的全部设备变量（聚合模板定义与实例配置）
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    [HttpGet("by-device/{deviceId}")]
    public async Task<IActionResult> GetByDevice(int deviceId) =>
        Ok(await _appService.GetByDeviceAsync(deviceId));

    /// <summary>
    /// 更新设备变量的实例配置：修改变量地址、位偏移、采集周期、启用/禁用、缩放/死区覆盖
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] DeviceVariableDto dto) =>
        Ok(await _appService.UpdateAsync(dto));
}
