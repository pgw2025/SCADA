using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 设备连接管理控制器（阶段 3：连接/控制器管理 API，路由 /api/device-connections）。
    /// <para>
    /// 负责 DeviceConnection 资产的管理：按控制器查询连接、CRUD。
    /// 写操作（创建/更新/删除）需 RequireAdmin 并记操作日志；查询仅需登录（全局 FallbackPolicy 默认认证）。
    /// </para>
    /// <para>
    /// 引用语义：被设备引用（Device.ConnectionId 指向）的连接不可在本端点更新/删除——
    /// 设备连接的生命周期（含与 Device.JsonConfig 的双写一致）由设备管理接口单点维护。
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/device-connections")]
    public class DeviceConnectionController : ApiControllerBase
    {
        private readonly IDeviceConnectionAppService _appService;

        public DeviceConnectionController(IDeviceConnectionAppService appService)
        {
            _appService = appService;
        }

        /// <summary>查询连接列表；controllerId 非空时仅返回该控制器下的连接。</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? controllerId = null)
            => Ok(await _appService.GetListAsync(controllerId));

        /// <summary>按 ID 获取连接。</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _appService.GetByIdAsync(id);
            return result == null ? NotFound() : Ok(result);
        }

        /// <summary>创建连接（校验控制器存在且启用、协议存在与配置 JSON 合法）。</summary>
        [HttpPost]
        [Authorize(Policy = "RequireAdmin")]
        [AuditLog("连接管理", "CREATE")]
        public async Task<IActionResult> Create([FromBody] CreateDeviceConnectionDto dto)
        {
            var bodyErr = EnsureBody(dto, "连接请求体不能为空");
            if (bodyErr != null) return bodyErr;

            return Ok(await _appService.CreateAsync(dto));
        }

        /// <summary>更新连接（被设备引用时拒绝，提示走设备管理页）。</summary>
        [HttpPut("{id:int}")]
        [Authorize(Policy = "RequireAdmin")]
        [AuditLog("连接管理", "UPDATE")]
        public async Task<IActionResult> Update(int id, [FromBody] CreateDeviceConnectionDto dto)
        {
            var bodyErr = EnsureBody(dto, "连接请求体不能为空");
            if (bodyErr != null) return bodyErr;

            return Ok(await _appService.UpdateAsync(id, dto));
        }

        /// <summary>删除连接（被设备引用时拒绝；删除后清理无引用的独占控制器）。</summary>
        [HttpDelete("{id:int}")]
        [Authorize(Policy = "RequireAdmin")]
        [AuditLog("连接管理", "DELETE")]
        public async Task<IActionResult> Delete(int id)
        {
            await _appService.DeleteAsync(id);
            return Ok(new { success = true, message = "连接删除成功" });
        }
    }
}
