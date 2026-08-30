using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using ScadaServer.Domain.Constants;
using ScadaServer.Runtime.Scripting;
using ScadaServer.WebApi.Filters;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 组态运行端脚本触发控制器：供 HMI「执行脚本」按钮在运行态调用。
    /// 与 SystemScriptController（管理端，RequireAdmin）区分：本端点放开到 Operator/Admin，
    /// 与变量写入（DeviceController.WriteVariable）权限口径一致；其它角色（如 Viewer）仍被拒绝（403）。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ScriptRuntimeController : ControllerBase
    {
        private readonly IScriptEngineHost _scriptEngine;

        public ScriptRuntimeController(IScriptEngineHost scriptEngine)
        {
            _scriptEngine = scriptEngine;
        }

        /// <summary>
        /// 触发执行指定系统脚本（服务端 Jint 沙箱）。返回本执行结果（含 log 输出）。
        /// </summary>
        /// <param name="id">系统脚本 ID</param>
        [HttpPost("{id}/run")]
        [Authorize(Roles = $"{SystemRoles.Operator},{SystemRoles.Admin}")]
        [AuditLog("脚本触发", "WRITE")]
        public async Task<IActionResult> Run(int id)
        {
            var executor = User.FindFirst(ClaimTypes.Name)?.Value
                           ?? User.FindFirst("username")?.Value
                           ?? User.Identity?.Name
                           ?? "unknown";
            var result = await _scriptEngine.RunAsync(id, executor);
            return Ok(result);
        }
    }
}
