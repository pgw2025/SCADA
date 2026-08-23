using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;
using ScadaServer.Infrastructure.Services;

namespace ScadaServer.WebApi.Controllers;

/// <summary>
/// 系统控制器，提供系统状态监控接口
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly SystemMonitorService _monitorService;

    /// <summary>
    /// 初始化系统控制器
    /// </summary>
    /// <param name="monitorService">系统监控服务</param>
    public SystemController(SystemMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    /// <summary>
    /// 获取系统状态
    /// </summary>
    /// <returns>系统状态DTO</returns>
    [HttpGet("status")]
    public ActionResult<SystemStatusDto> GetStatus()
    {
        return Ok(_monitorService.CurrentStatus);
    }
}
