using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Constants;
using ScadaServer.Domain.Enums;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Runtime.Interface;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 设备运行时控制器：提供聚合快照查询与机器运行状态（RunState）置位。
    /// 与设备 CRUD 控制器（DeviceController）语义分开——本控制器面向"运行时态"而非"配置"。
    /// </summary>
    [ApiController]
    [Route("api/devices")]
    public class RuntimeController : ApiControllerBase
    {
        private readonly IRuntimeStatusProvider _runtimeStatusProvider;
        private readonly IRuntimeManager _runtimeManager;
        private readonly ScadaDbContext _db;

        public RuntimeController(
            IRuntimeStatusProvider runtimeStatusProvider,
            IRuntimeManager runtimeManager,
            ScadaDbContext db)
        {
            _runtimeStatusProvider = runtimeStatusProvider;
            _runtimeManager = runtimeManager;
            _db = db;
        }

        /// <summary>
        /// 获取设备运行时聚合快照（连接态 / 运行态 / 报警 / 通信统计）。
        /// 前端进入设备详情页时一次拉取双状态与统计作为首帧；实时变化仍走 SignalR（变量值）。
        /// </summary>
        /// <param name="id">设备 ID</param>
        /// <response code="200">快照</response>
        /// <response code="404">设备不在运行中（禁用/初始化失败/重连窗口期）</response>
        [HttpGet("{id:int}/runtime")]
        [Authorize]
        public IActionResult GetRuntimeSnapshot(int id)
        {
            if (_runtimeStatusProvider.TryGetRuntimeSnapshot(id, out var snapshot))
                return Ok(snapshot);

            // D5-a：未注册（禁用/初始化失败/重连窗口）→ 404。
            // 调用方判断设备存在性应使用 GET /api/devices。
            return NotFound(new { message = $"设备 {id} 不在运行中（可能已禁用或正在重连）。" });
        }

        /// <summary>
        /// 置位设备运行状态（机器状态，人工置位）。持久化并即时更新运行时内存。
        /// 禁用设备也允许置位（维护常发生在停机设备上），待设备启用时 RestoreRunState 自动生效（方案 P3）。
        /// </summary>
        /// <param name="id">设备 ID</param>
        /// <param name="request">运行状态请求体</param>
        /// <response code="200">置位成功（含 changedAt UTC）</response>
        /// <response code="400">非法枚举值</response>
        /// <response code="404">设备不存在</response>
        [HttpPut("{id:int}/runtime/runstate")]
        [Authorize(Roles = $"{SystemRoles.Operator},{SystemRoles.Admin}")]
        [AuditLog("设备运行状态", "SET_RUNSTATE")]
        public async Task<IActionResult> SetRunState(int id, [FromBody] SetRunStateRequest request)
        {
            // 1) 枚举合法性：非法字符串 → 400
            if (!Enum.TryParse<DeviceRunState>(request.RunState, ignoreCase: true, out var runState)
                || !Enum.IsDefined(runState))
            {
                return BadRequest(new { message = $"无效的运行状态：{request.RunState}" });
            }

            // 2) 设备存在性：查 DB（设备可能不在运行时中——禁用设备也允许置位，重启后生效）
            //    ExecuteUpdate 返回影响行数 = 0 → 404
            var now = DateTime.UtcNow;
            var affected = await _db.Devices
                .Where(d => d.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.RunState, runState)
                    .SetProperty(d => d.RunStateChangedAt, now));
            if (affected == 0)
                return NotFound(new { message = $"设备 {id} 不存在。" });

            // 3) 运行时内存即时生效（设备在册时）
            _runtimeManager.SetDeviceRunState(id, runState);

            // 4) 不推送 SignalR（D10-a）：RunState 变化由前端轮询快照感知
            return Ok(new { deviceId = id, runState = runState.ToString(), changedAt = now });
        }
    }

    /// <summary>置位运行状态请求体。</summary>
    public class SetRunStateRequest
    {
        /// <summary>DeviceRunState 枚举名字符串（与 SignalR/REST 既有"字符串枚举"序列化约定一致），忽略大小写。</summary>
        public string RunState { get; set; } = string.Empty;
    }
}