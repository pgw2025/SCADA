using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers;

/// <summary>
/// 设备变量控制器：维护变量在设备上的实例配置（地址、位偏移、轮询间隔、启用状态、缩放/死区）。
/// 与 DataPoint（变量模板）相互独立——本控制器操作的是"某设备上的某变量实例"。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireAdmin")]
public class DataPointMappingController : ControllerBase
{
    private readonly IDataPointMappingAppService _appService;

    public DataPointMappingController(IDataPointMappingAppService appService)
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
    /// 创建设备变量实例（按"设备 + 变量模板"）。模板新增变量后，可为已存在设备补齐对应变量实例。
    /// </summary>
    [HttpPost]
    [AuditLog("设备变量", "CREATE")]
    public async Task<IActionResult> Create([FromBody] CreateDataPointMappingDto dto) =>
        Ok(await _appService.CreateAsync(dto));

    /// <summary>
    /// 删除设备变量实例
    /// </summary>
    /// <param name="id">设备变量ID</param>
    [HttpDelete("{id}")]
    [AuditLog("设备变量", "DELETE")]
    public async Task<IActionResult> Delete(int id)
    {
        await _appService.DeleteAsync(id);
        return Ok(new { success = true, message = "设备变量删除成功" });
    }

    /// <summary>
    /// 更新设备变量的实例配置：修改变量地址、位偏移、采集周期、启用/禁用、缩放/死区覆盖
    /// </summary>
    [HttpPut]
    [AuditLog("设备变量", "UPDATE")]
    public async Task<IActionResult> Update([FromBody] DataPointMappingDto dto) =>
        Ok(await _appService.UpdateAsync(dto));
}
