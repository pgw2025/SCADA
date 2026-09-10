using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 消息通知配置控制器（钉钉群机器人 / SMTP 邮件）。
    /// 读写统一走 override 文件（重启后生效）；测试发送使用临时值，不改变生效配置。
    /// 投递记录：三渠道真实投递终态的查询/清空/重试。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class NotificationConfigController : ControllerBase
    {
        private readonly INotificationConfigService _service;
        private readonly INotificationLogService _logService;

        public NotificationConfigController(
            INotificationConfigService service,
            INotificationLogService logService)
        {
            _service = service;
            _logService = logService;
        }

        /// <summary>获取当前通知配置（敏感字段以掩码回显）</summary>
        [HttpGet]
        public async Task<IActionResult> Get() => Ok(await _service.GetAsync());

        /// <summary>保存通知配置到 override 文件（重启后生效；密钥/授权码掩码 = 不改）</summary>
        [HttpPut]
        public async Task<IActionResult> Save([FromBody] NotificationConfigDto dto)
        {
            await _service.SaveAsync(dto);
            return Ok();
        }

        /// <summary>测试发送钉钉机器人消息（使用提交的临时值，不落盘）</summary>
        [HttpPost("test-dingtalk")]
        public async Task<IActionResult> TestDingTalk([FromBody] DingTalkConfigDto dto)
            => Ok(await _service.TestDingTalkAsync(dto));

        /// <summary>测试发送通知邮件（使用提交的临时值，不落盘）</summary>
        [HttpPost("test-email")]
        public async Task<IActionResult> TestEmail([FromBody] EmailConfigDto dto)
            => Ok(await _service.TestEmailAsync(dto));

        /// <summary>测试发送企业微信群机器人消息（使用提交的临时值，不落盘）</summary>
        [HttpPost("test-wecom")]
        public async Task<IActionResult> TestWeCom([FromBody] WeComConfigDto dto)
            => Ok(await _service.TestWeComAsync(dto));

        /// <summary>查询最近投递记录（默认 500 条，Id 倒序，含三渠道真实投递与测试发送）</summary>
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs([FromQuery] int limit = 500)
            => Ok(await _logService.GetRecentAsync(Math.Clamp(limit, 1, 2000)));

        /// <summary>清空全部投递记录（同时丢弃仍在待写队列中的记录）</summary>
        [HttpPost("logs/clear")]
        public async Task<IActionResult> ClearLogs(
            [FromServices] INotificationLogRecorder logRecorder)
        {
            await _logService.ClearAsync();
            logRecorder.DiscardPending();
            return Ok();
        }

        /// <summary>
        /// 重试一条失败记录（复用队列管线，定向到原渠道；行置 Retrying，管线完成后回写终态）。
        /// 仅 Status=Failed 且载荷完整的行可重试；渠道未启用/队列满载时抛业务异常并保持行状态一致。
        /// </summary>
        [HttpPost("logs/{id}/retry")]
        public async Task<IActionResult> RetryLog(long id)
            => Ok(await _logService.RetryAsync(id));
    }
}
