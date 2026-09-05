using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Infrastructure.Persistence;
using WebPush;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// Web Push 发送渠道（doc/pwa 阶段五 · 步骤 23，D7/D8，web-push-csharp）。
    /// <para>
    /// 作为第三渠道接入既有通知管线（与钉钉/邮件并列），自动获得限流/合并/退避治理。
    /// SendAsync 职责：级别过滤 -> 查有效订阅（含订阅者角色） -> 逐订阅构造 payload
    /// （landingUrl 按角色，D6） -> 并发推送（SemaphoreSlim） -> 410/404 订阅清理。
    /// </para>
    /// <para>
    /// 日志纪律：订阅级失败仅 Warning/Information（运维噪声），绝不抛出——渠道健康由管线管理。
    /// 单条推送超时收敛在管线 22s 排空预算内；SendAsync 尊重 cancellationToken（优雅停机）。
    /// </para>
    /// </summary>
    public class WebPushSender : IExternalMessageSender
    {
        /// <summary>本渠道日志类别前缀（SystemLogRecorder 递归防护对齐管线既有约定）。</summary>
        public const string LoggerCategory = "ScadaServer.Infrastructure.Communication.WebPushSender";

        /// <summary>测试推送标记（POST /api/push/test 注入，绕过级别过滤且仅发本人订阅）。</summary>
        public const string TokenTest = "test";

        /// <summary>测试推送目标用户（Tokens 键）。</summary>
        public const string TokenTargetUserId = "targetUserId";

        private readonly WebPushOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WebPushSender> _logger;

        public WebPushSender(
            IOptions<NotificationOptions> options,
            IServiceScopeFactory scopeFactory,
            ILogger<WebPushSender> logger)
        {
            _options = options.Value.WebPush;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public string Name => "WebPush";

        /// <summary>Enabled 且 VAPID 密钥完整（缺密钥启动时渠道禁用，管线不扇出——与既有渠道语义一致）。</summary>
        public bool Enabled => _options.Enabled
            && !string.IsNullOrWhiteSpace(_options.Vapid.Subject)
            && !string.IsNullOrWhiteSpace(_options.Vapid.PublicKey)
            && !string.IsNullOrWhiteSpace(_options.Vapid.PrivateKey);

        public async Task SendAsync(ExternalMessage message, CancellationToken cancellationToken)
        {
            if (!Enabled) return;

            var tokens = message.Tokens ?? new Dictionary<string, string?>();

            // 测试推送：仅发目标用户本人订阅，绕过级别过滤（步骤 17 UI「发送测试通知」依赖）
            var isTest = tokens.TryGetValue(TokenTest, out var t) && t == "true";
            int? targetUserId = null;
            if (isTest && tokens.TryGetValue(TokenTargetUserId, out var uidStr) && int.TryParse(uidStr, out var uid))
            {
                targetUserId = uid;
            }
            else
            {
                // ① 级别过滤：Severity 低于阈值丢弃；恢复类通知按 PushRecover 过滤
                if (!PassesSeverityFilter(message)) return;
            }

            // ② 查询订阅（含订阅者角色，逐订阅计算落地页 D6）
            List<(Domain.Entities.PushSubscription Sub, string Role)> subs;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                var query = db.PushSubscriptions.AsNoTracking();
                if (targetUserId.HasValue)
                {
                    query = query.Where(s => s.UserId == targetUserId.Value);
                }

                subs = await query
                    .Join(db.SystemUsers.AsNoTracking(),
                        s => s.UserId,
                        u => u.Id,
                        (s, u) => new { Sub = s, Role = u.Role })
                    .ToListAsync(cancellationToken)
                    .ContinueWith(t => t.Result.Select(x => (x.Sub, x.Role)).ToList(), cancellationToken);
            }

            if (subs.Count == 0)
            {
                _logger.LogInformation("WebPush：无有效订阅，消息未推送（{Title}）。", message.Title);
                return;
            }

            var vapid = new VapidDetails(_options.Vapid.Subject, _options.Vapid.PublicKey, _options.Vapid.PrivateKey);
            var maxConcurrency = Math.Max(1, _options.MaxConcurrentSends);
            using var gate = new SemaphoreSlim(maxConcurrency);

            var okIds = new List<long>();
            var goneIds = new List<long>();
            var failedIds = new List<(long Id, string Code)>();

            // ③④ 逐订阅并发推送（限并发）；payload 按订阅者角色构造（landingUrl 差异化，D6）
            var tasks = subs.Select(item => Task.Run(async () =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var payloadJson = System.Text.Json.JsonSerializer.Serialize(
                        BuildPayload(message, tokens, isTest, item.Role));
                    await SendToSubscriptionAsync(item.Sub, payloadJson, vapid, cancellationToken);
                    lock (okIds) { okIds.Add(item.Sub.Id); }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 停机取消：静默退出
                }
                catch (WebPushException wex) when (
                    wex.StatusCode == HttpStatusCode.Gone || wex.StatusCode == HttpStatusCode.NotFound)
                {
                    // ⑤ 过期/失效订阅：删除（Information 日志，步骤 23 验收项）
                    lock (goneIds) { goneIds.Add(item.Sub.Id); }
                    _logger.LogInformation("WebPush：订阅已失效（{Status}），清理 Endpoint={Endpoint}",
                        wex.StatusCode, EndpointSummary(item.Sub.Endpoint));
                }
                catch (WebPushException wex)
                {
                    lock (failedIds) { failedIds.Add((item.Sub.Id, ((int?)wex.StatusCode)?.ToString() ?? wex.GetType().Name)); }
                    _logger.LogWarning(wex, "WebPush：订阅推送失败（{Status}）Endpoint={Endpoint}",
                        wex.StatusCode, EndpointSummary(item.Sub.Endpoint));
                }
                catch (HttpRequestException hex)
                {
                    lock (failedIds) { failedIds.Add((item.Sub.Id, "HttpError")); }
                    _logger.LogWarning(hex, "WebPush：推送网络失败 Endpoint={Endpoint}", EndpointSummary(item.Sub.Endpoint));
                }
                catch (Exception ex)
                {
                    lock (failedIds) { failedIds.Add((item.Sub.Id, "Unknown")); }
                    _logger.LogWarning(ex, "WebPush：推送未知异常 Endpoint={Endpoint}", EndpointSummary(item.Sub.Endpoint));
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken)).ToArray();

            await Task.WhenAll(tasks);

            // ⑥ 结果落库（成功时间 / 失败码 / 失效清理）——尽力而为，失败不抛出
            await PersistResultsAsync(okIds, goneIds, failedIds, cancellationToken);
        }

        /// <summary>发送单条（WebPushClient 非线程安全，每订阅独立实例）。</summary>
        private static async Task SendToSubscriptionAsync(
            Domain.Entities.PushSubscription sub, string payloadJson, VapidDetails vapid, CancellationToken ct)
        {
            using var client = new WebPushClient();
            var subscription = new PushSubscription(sub.Endpoint, sub.P256DH, sub.Auth);
            await client.SendNotificationAsync(subscription, payloadJson, vapid, ct);
        }

        /// <summary>级别过滤：解析 MinSeverity 排名（兼容报警四级与通用四级命名），低于阈值不推；
        /// 恢复类通知按 PushRecover 开关过滤（默认不推，防噪音）。</summary>
        private bool PassesSeverityFilter(ExternalMessage message)
        {
            var isRecover = message.Tokens?.TryGetValue("eventType", out var evt) == true && evt == "Recovered";
            if (isRecover && !_options.PushRecover)
            {
                return false;
            }

            return SeverityRank(message.Severity) >= SeverityRank(_options.MinSeverity);
        }

        /// <summary>级别排名：Info/Low=0、Warning/Medium=1、Error/High=2、Critical=3（未知=0，保守不推）。</summary>
        private static int SeverityRank(string? severity) => severity?.Trim().ToUpperInvariant() switch
        {
            "INFO" or "LOW" => 0,
            "WARNING" or "MEDIUM" => 1,
            "ERROR" or "HIGH" => 2,
            "CRITICAL" => 3,
            _ => 0
        };

        /// <summary>构造推送 payload（§5.4 契约，UTF-8 JSON ≤2KB）；landingUrl 按订阅者角色计算（D6）。</summary>
        private Dictionary<string, object?> BuildPayload(
            ExternalMessage message, Dictionary<string, string?> tokens, bool isTest, string subscriberRole)
        {
            string? Get(string key) => tokens.TryGetValue(key, out var v) ? v : null;

            var tag = isTest
                ? "push-test"
                : message.Category == ExternalMessageCategory.Alarm
                    ? $"alarm:{Get("deviceId")}:{Get("variableKey")}"
                    : message.Category.ToString().ToLowerInvariant();

            var body = isTest
                ? "这是一条测试推送。收到即说明 Web Push 链路正常。"
                : BuildSummaryBody(message, tokens);

            // 角色落地页：Admin → /alarm-management；其余/角色缺失 → /scada-view（D6）
            var landingUrl = string.Equals(subscriberRole?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase)
                ? "/alarm-management"
                : "/scada-view";

            return new Dictionary<string, object?>
            {
                ["title"] = message.Title,
                ["body"] = body,
                ["tag"] = tag,
                ["severity"] = message.Severity ?? string.Empty,
                ["landingUrl"] = landingUrl,
                ["timestampUtc"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            };
        }

        /// <summary>结构化摘要正文（非 Markdown）：从 tokens 组装「设备·变量 实际值 阈值 · 时间」。</summary>
        private static string BuildSummaryBody(ExternalMessage message, Dictionary<string, string?> tokens)
        {
            string? Get(string key) => tokens.TryGetValue(key, out var v) ? v : null;

            var parts = new List<string>();
            var device = Get("deviceKey") ?? (Get("deviceId") != null ? $"#{Get("deviceId")}" : null);
            if (!string.IsNullOrEmpty(device)) parts.Add(device!);
            var variable = Get("variableName") ?? Get("variableKey");
            if (!string.IsNullOrEmpty(variable)) parts.Add(variable!);
            if (!string.IsNullOrEmpty(Get("actualValue"))) parts.Add($"实际值 {Get("actualValue")}");
            if (!string.IsNullOrEmpty(Get("threshold"))) parts.Add($"阈值 {Get("threshold")}");
            if (!string.IsNullOrEmpty(Get("status"))) parts.Add($"状态 {Get("status")}");
            if (!string.IsNullOrEmpty(Get("message"))) parts.Add(Get("message")!);

            var summary = string.Join(" · ", parts);
            if (string.IsNullOrWhiteSpace(summary))
            {
                // 非 Alarm 类兜底：剥离 Markdown 标记的纯文本摘要（≤180 字符）
                summary = StripMarkdown(message.MarkdownText);
            }
            if (!string.IsNullOrEmpty(Get("time"))) summary += $" · {Get("time")}";
            return summary.Length > 180 ? summary[..180] : summary;
        }

        private static string StripMarkdown(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
            var lines = markdown.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith("|") && !l.StartsWith("<"));
            var text = string.Join(" ", lines)
                .Replace("**", string.Empty).Replace("*", string.Empty).Replace("`", string.Empty);
            return text.Length > 180 ? text[..180] : text;
        }

        /// <summary>结果落库：成功更新 LastPushAtUtc；失败记 LastErrorCode；410/404 删除。尽力而为不抛出。</summary>
        private async Task PersistResultsAsync(
            List<long> okIds, List<long> goneIds, List<(long Id, string Code)> failedIds, CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

                if (goneIds.Count > 0)
                {
                    var gone = await db.PushSubscriptions
                        .Where(s => goneIds.Contains(s.Id))
                        .ToListAsync(ct);
                    db.PushSubscriptions.RemoveRange(gone);
                }

                if (okIds.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    await db.PushSubscriptions
                        .Where(s => okIds.Contains(s.Id))
                        .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastPushAtUtc, now), ct);
                }

                foreach (var (id, code) in failedIds)
                {
                    var errorCode = code.Length > 64 ? code[..64] : code;
                    await db.PushSubscriptions
                        .Where(s => s.Id == id)
                        .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastErrorCode, errorCode), ct);
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebPush：推送结果落库失败（不影响渠道健康）。");
            }
        }

        /// <summary>Endpoint 摘要（日志脱敏：只留末段标识，不落完整 URL 与 payload）。</summary>
        private static string EndpointSummary(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint)) return string.Empty;
            return endpoint.Length <= 16 ? endpoint : $"…{endpoint[^16..]}";
        }
    }
}
