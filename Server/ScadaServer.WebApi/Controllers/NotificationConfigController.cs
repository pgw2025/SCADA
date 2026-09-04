using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// 消息通知配置控制器（钉钉群机器人 / SMTP 邮件）。
    /// 读写统一走 override 文件（重启后生效）；测试发送使用临时值，不改变生效配置。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")]
    public class NotificationConfigController : ControllerBase
    {
        private readonly INotificationConfigService _service;

        public NotificationConfigController(INotificationConfigService service)
        {
            _service = service;
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
    }
}
