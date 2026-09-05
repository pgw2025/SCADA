using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.Controllers
{
    /// <summary>
    /// Web Push 订阅管理端点（doc/pwa 阶段五 · 步骤 22，§5.6）。
    /// <para>
    /// 权限边界：订阅管理要求已登录（Admin/Operator/Viewer 均可——操作工是推送主要受众）；
    /// renew 端点凭续订令牌鉴权（SW 后台续订无 JWT，D12）；DELETE 严格本人归属校验。
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/push")]
    [Authorize]
    public class PushSubscriptionController : ApiControllerBase
    {
        private readonly ScadaDbContext _db;
        private readonly NotificationOptions _options;
        private readonly IEnumerable<IExternalMessageSender> _senders;

        public PushSubscriptionController(
            ScadaDbContext db,
            IOptions<NotificationOptions> options,
            IEnumerable<IExternalMessageSender> senders)
        {
            _db = db;
            _options = options.Value;
            _senders = senders;
        }

        /// <summary>VAPID 公钥（公钥无需保密；设置页/订阅流程拉取）。</summary>
        [HttpGet("vapid-public-key")]
        public IActionResult GetVapidPublicKey()
        {
            var vapid = _options.WebPush.Vapid;
            var enabled = _options.WebPush.Enabled
                && !string.IsNullOrWhiteSpace(vapid.PublicKey);
            if (!enabled)
            {
                return Ok(ApiResponse.Fail("推送服务未启用"));
            }
            return Ok(ApiResponse.Ok(new { publicKey = vapid.PublicKey }));
        }

        /// <summary>
        /// 创建/换绑订阅（D3）：以 Endpoint 为唯一键 upsert——
        /// 已存在则把 UserId 更新为当前用户并重签续订令牌；不存在则新建。
        /// </summary>
        [HttpPost("subscriptions")]
        public async Task<IActionResult> UpsertSubscription([FromBody] PushSubscriptionDto? dto)
        {
            var bodyErr = EnsureBody(dto, "订阅信息缺失");
            if (bodyErr != null) return bodyErr;
            if (string.IsNullOrWhiteSpace(dto!.Endpoint) || dto.Endpoint.Length > 512)
            {
                return BadRequest(ApiResponse.Fail("endpoint 非法"));
            }

            var userId = CurrentUserId();
            if (userId is null) return Unauthorized(new { Message = "无法识别当前用户" });

            var sub = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint);
            var isNew = sub is null;
            if (sub is null)
            {
                sub = new PushSubscription { Endpoint = dto.Endpoint };
                _db.PushSubscriptions.Add(sub);
            }

            sub.UserId = userId.Value;
            sub.P256DH = Truncate(dto.P256DH, 256) ?? string.Empty;
            sub.Auth = Truncate(dto.Auth, 256) ?? string.Empty;
            sub.ExpirationTime = dto.ExpirationTime;
            sub.RenewalToken = NewRenewalToken();
            if (isNew)
            {
                sub.UserAgent = Truncate(Request.Headers.UserAgent.ToString(), 256) ?? string.Empty;
                sub.CreatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return Ok(ApiResponse.Ok(new { bound = true, renewalToken = sub.RenewalToken }));
        }

        /// <summary>本人订阅列表（设置页「我的设备」回显）。</summary>
        [HttpGet("subscriptions/me")]
        public async Task<IActionResult> MySubscriptions()
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized(new { Message = "无法识别当前用户" });

            var list = await _db.PushSubscriptions.AsNoTracking()
                .Where(s => s.UserId == userId.Value)
                .OrderByDescending(s => s.CreatedAtUtc)
                .Select(s => new
                {
                    s.Endpoint,
                    s.UserAgent,
                    s.CreatedAtUtc,
                    s.LastPushAtUtc,
                    s.LastErrorCode
                })
                .ToListAsync();
            return Ok(ApiResponse.Ok(new { list }));
        }

        /// <summary>
        /// 退订（本人订阅才可删）：带 endpoint 查询参数删单设备，不带删本人全部订阅。
        /// </summary>
        [HttpDelete("subscriptions")]
        public async Task<IActionResult> DeleteSubscription([FromQuery] string? endpoint)
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized(new { Message = "无法识别当前用户" });

            var query = _db.PushSubscriptions.Where(s => s.UserId == userId.Value);
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                query = query.Where(s => s.Endpoint == endpoint);
            }

            var subs = await query.ToListAsync();
            if (string.IsNullOrWhiteSpace(endpoint) && subs.Count == 0)
            {
                return Ok(ApiResponse.Ok(new { removed = 0 }));
            }
            if (subs.Count == 0)
            {
                return NotFound(ApiResponse.Fail("订阅不存在"));
            }

            _db.PushSubscriptions.RemoveRange(subs);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse.Ok(new { removed = subs.Count }));
        }

        /// <summary>
        /// SW 自动续订专用（D12）：凭续订令牌鉴权（不要求 JWT），更新订阅键值并重签令牌。
        /// </summary>
        [HttpPost("subscriptions/renew")]
        [AllowAnonymous]
        public async Task<IActionResult> RenewSubscription([FromBody] PushSubscriptionDto? dto)
        {
            var bodyErr = EnsureBody(dto, "订阅信息缺失");
            if (bodyErr != null) return bodyErr;

            var token = Request.Headers["X-Renewal-Token"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(dto!.Endpoint))
            {
                return Unauthorized(ApiResponse.Fail("续订凭证缺失"));
            }

            var sub = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.RenewalToken == token);
            if (sub is null)
            {
                // 令牌失效：401，SW 端静默放弃，等待用户下次打开应用走正常订阅流程
                return Unauthorized(ApiResponse.Fail("续订凭证无效"));
            }

            sub.Endpoint = dto.Endpoint;
            sub.P256DH = Truncate(dto.P256DH, 256) ?? string.Empty;
            sub.Auth = Truncate(dto.Auth, 256) ?? string.Empty;
            sub.ExpirationTime = dto.ExpirationTime;
            sub.RenewalToken = NewRenewalToken();
            await _db.SaveChangesAsync();
            return Ok(ApiResponse.Ok(new { bound = true }));
        }

        /// <summary>向本人全部有效订阅发送测试推送（设置页「发送测试通知」按钮，步骤 17）。</summary>
        [HttpPost("test")]
        public async Task<IActionResult> SendTest()
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized(new { Message = "无法识别当前用户" });

            var sender = _senders.FirstOrDefault(s => s.Name == "WebPush");
            if (sender is null || !sender.Enabled)
            {
                return Ok(ApiResponse.Fail("Web Push 渠道未启用（请检查 VAPID 配置）"));
            }

            var hasSub = await _db.PushSubscriptions.AsNoTracking().AnyAsync(s => s.UserId == userId.Value);
            if (!hasSub)
            {
                return Ok(ApiResponse.Fail("当前账号在本服务器暂无订阅，请先开启推送"));
            }

            var message = new ExternalMessage
            {
                Category = ExternalMessageCategory.SystemAlarm,
                Title = "🔔 测试推送",
                MarkdownText = "Web Push 链路测试通知",
                Severity = "Info",
                Tokens = new Dictionary<string, string?>
                {
                    { WebPushSender.TokenTest, "true" },
                    { WebPushSender.TokenTargetUserId, userId.Value.ToString() },
                    { "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                }
            };
            await sender.SendAsync(message, HttpContext.RequestAborted);
            return Ok(ApiResponse.Ok(new { sent = true }));
        }

        // ---- 内部辅助 ----

        private int? CurrentUserId()
            => int.TryParse(User.FindFirst("id")?.Value, out var id) ? id : null;

        private static string NewRenewalToken()
            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        private static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value[..max];
        }
    }

    /// <summary>订阅 upsert/renew 请求体（§5.6）。</summary>
    public class PushSubscriptionDto
    {
        public string Endpoint { get; set; } = string.Empty;
        public string? P256DH { get; set; }
        public string? Auth { get; set; }
        public DateTime? ExpirationTime { get; set; }
    }
}
