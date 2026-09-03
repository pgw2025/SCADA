using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 设备-数据模型绑定管理控制器（阶段 5：多对多绑定，路由 /api/devices/{deviceId}/data-models）。
    /// <para>
    /// 设备详情以 RESTful 子资源暴露模型绑定管理：查询全部绑定、绑定新模型、解绑、切换主模型。
    /// 写操作（绑定/解绑/切主）需 RequireAdmin 并记操作日志；查询仅需登录（全局 FallbackPolicy 默认认证）。
    /// 主模型（IsPrimary=true）与 Device.ModelId 的双写一致性由 DeviceDataModelAppService 事务单点维护。
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/devices/{deviceId:int}/data-models")]
    public class DeviceDataModelController : ApiControllerBase
    {
        private readonly IDeviceDataModelAppService _appService;

        public DeviceDataModelController(IDeviceDataModelAppService appService)
        {
            _appService = appService;
        }

        /// <summary>查询设备全部模型绑定（含模型摘要 Code/Name/Version 与模型变量数）。</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll(int deviceId)
            => Ok(await _appService.GetByDeviceAsync(deviceId));

        /// <summary>
        /// 绑定一个数据模型到设备；body.IsPrimary=true 时同时设为主模型
        /// （事务内降级旧主并同步 Device.ModelId，唯一双写点）。
        /// </summary>
        [HttpPost]
        [Authorize(Policy = "RequireAdmin")]
        [AuditLog("设备数据模型", "BIND")]
        public async Task<IActionResult> Bind(int deviceId, [FromBody] BindDeviceDataModelDto dto)
        {
            var bodyErr = EnsureBody(dto, "绑定请求体不能为空");
            if (bodyErr != null) return bodyErr;

            return Ok(await _appService.BindAsync(deviceId, dto));
        }

        /// <summary>切换主模型（目标必须是已绑定模型；事务内降级旧主并同步 Device.ModelId，唯一双写点）。</summary>
        [HttpPut("primary")]
        [Authorize(Policy = "RequireAdmin")]
        [AuditLog("设备数据模型", "SET_PRIMARY")]
        public async Task<IActionResult> SetPrimary(int deviceId, [FromBody] DeviceDataModelRequest request)
        {
            var bodyErr = EnsureBody(request, "请求体不能为空");
            if (bodyErr != null) return bodyErr;

            return Ok(await _appService.SetPrimaryAsync(deviceId, request));
        }

        /// <summary>解绑模型（主模型不可解绑；该模型下存在设备变量实例引用时拒绝并提示清理）。</summary>
        [HttpDelete("{dataModelId:int}")]
        [Authorize(Policy = "RequireAdmin")]
        [AuditLog("设备数据模型", "UNBIND")]
        public async Task<IActionResult> Unbind(int deviceId, int dataModelId)
            => Ok(await _appService.UnbindAsync(deviceId, dataModelId));
    }
}
