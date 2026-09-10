using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Infrastructure.Communication;

namespace ScadaServer.Infrastructure.Services
{
    /// <summary>
    /// 运行时消息通知配置管理服务（钉钉 / SMTP）。
    /// <para>
    /// 配置写入叠加文件 <c>appsettings.dboverride.json</c>（已在 Program.cs 叠加加载），
    /// 修改后需重启生效，与数据库主库配置一致。写入时合并读取现有文件，
    /// 仅更新 "Notification" 节，避免覆盖 SystemDbConfig 等其他运行期配置。
    /// </para>
    /// <para>
    /// 测试发送使用提交的临时值构造发送器，不修改当前生效配置（等价数据库"测试连接"）。
    /// </para>
    /// </summary>
    public class NotificationConfigService : INotificationConfigService
    {
        private const string OverrideFileName = "appsettings.dboverride.json";
        private const string SecretMask = "******";

        private readonly IOptionsMonitor<NotificationOptions> _current;
        private readonly IHostEnvironment _env;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly INotificationLogRecorder _logRecorder;
        private readonly ILogger<NotificationConfigService> _logger;

        public NotificationConfigService(
            IOptionsMonitor<NotificationOptions> current,
            IHostEnvironment env,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            INotificationLogRecorder logRecorder,
            ILogger<NotificationConfigService> logger)
        {
            _current = current;
            _env = env;
            _httpClientFactory = httpClientFactory;
            _loggerFactory = loggerFactory;
            _logRecorder = logRecorder;
            _logger = logger;
        }

        /// <inheritdoc/>
        public Task<NotificationConfigDto> GetAsync()
        {
            var o = _current.CurrentValue;
            return Task.FromResult(new NotificationConfigDto
            {
                DingTalk = new DingTalkConfigDto
                {
                    Enabled = o.DingTalk.Enabled,
                    Webhook = o.DingTalk.Webhook,
                    Secret = string.IsNullOrEmpty(o.DingTalk.Secret) ? string.Empty : SecretMask,
                    HasSecret = !string.IsNullOrEmpty(o.DingTalk.Secret)
                },
                Email = new EmailConfigDto
                {
                    Enabled = o.Email.Enabled,
                    SmtpHost = o.Email.SmtpHost,
                    SmtpPort = o.Email.SmtpPort,
                    UseSsl = o.Email.UseSsl,
                    Username = o.Email.Username,
                    Password = string.IsNullOrEmpty(o.Email.Password) ? string.Empty : SecretMask,
                    HasPassword = !string.IsNullOrEmpty(o.Email.Password),
                    From = o.Email.From,
                    FromName = o.Email.FromName,
                    To = o.Email.To?.ToList() ?? new List<string>()
                },
                WeCom = new WeComConfigDto
                {
                    Enabled = o.WeCom.Enabled,
                    Webhook = o.WeCom.Webhook
                },
                Push = o.Push,
                Templates = o.Templates
            });
        }

        /// <inheritdoc/>
        public async Task SaveAsync(NotificationConfigDto dto)
        {
            if (dto == null)
            {
                throw new BusinessException("通知配置不能为空。");
            }

            // 敏感项（密钥/授权码）掩码或空 => 保持旧值不变
            var current = _current.CurrentValue;
            var secret = ResolveSecret(dto.DingTalk?.Secret, current.DingTalk.Secret, dto.DingTalk?.HasSecret == true);
            var password = ResolveSecret(dto.Email?.Password, current.Email.Password, dto.Email?.HasPassword == true);

            var merged = new NotificationOptions
            {
                DingTalk = new DingTalkOptions
                {
                    Enabled = dto.DingTalk?.Enabled ?? false,
                    Webhook = (dto.DingTalk?.Webhook ?? string.Empty).Trim(),
                    Secret = secret
                },
                Email = new EmailOptions
                {
                    Enabled = dto.Email?.Enabled ?? false,
                    SmtpHost = (dto.Email?.SmtpHost ?? string.Empty).Trim(),
                    SmtpPort = dto.Email?.SmtpPort ?? 465,
                    UseSsl = dto.Email?.UseSsl ?? true,
                    Username = (dto.Email?.Username ?? string.Empty).Trim(),
                    Password = password,
                    From = (dto.Email?.From ?? string.Empty).Trim(),
                    FromName = string.IsNullOrWhiteSpace(dto.Email?.FromName) ? "SCADA 报警中心" : dto.Email.FromName.Trim(),
                    To = dto.Email?.To?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList() ?? new List<string>()
                },
                // WeCom：请求体未携带该片段（旧客户端/脚本）时沿用旧值，防止已启用渠道被静默重置
                WeCom = dto.WeCom is null
                    ? current.WeCom
                    : new WeComOptions
                    {
                        Enabled = dto.WeCom.Enabled,
                        Webhook = (dto.WeCom.Webhook ?? string.Empty).Trim()
                    },
                // 完整保留 Push 策略（前端回传整段对象，含未编辑的高级参数）
                Push = dto.Push ?? current.Push,
                // 模板：未传/空则沿用旧值，避免覆盖已保存的自定义模板
                Templates = dto.Templates ?? current.Templates
            };

            if (merged.DingTalk.Enabled && string.IsNullOrWhiteSpace(merged.DingTalk.Webhook))
            {
                throw new BusinessException("启用钉钉通知时必须填写 Webhook 地址。");
            }
            if (merged.Email.Enabled &&
                (string.IsNullOrWhiteSpace(merged.Email.SmtpHost) ||
                 string.IsNullOrWhiteSpace(merged.Email.Username) ||
                 string.IsNullOrWhiteSpace(merged.Email.From) ||
                 merged.Email.To.Count == 0))
            {
                throw new BusinessException("启用邮件通知时必须填写 SMTP 主机/账号/发件人/收件人（至少一项）。");
            }

            foreach (var to in merged.Email.To)
            {
                if (!TryParseAddress(to))
                {
                    throw new BusinessException($"收件人邮箱格式不正确：{to}");
                }
            }

            if (merged.WeCom.Enabled && string.IsNullOrWhiteSpace(merged.WeCom.Webhook))
            {
                throw new BusinessException("启用企业微信通知时必须填写 Webhook 地址。");
            }

            var path = GetOverridePath();
            var root = await ReadOverrideRootAsync();

            // 节点级合并（v2）：只覆写本服务管理的五个子节，保留 override 文件中其他子节
            // （WebPush/VAPID 等）。原「root["Notification"] = payload["Notification"]」整体替换会把
            // WebPush 节（含 VAPID 私钥）一并抹掉——接入 WeCom 前的既有隐患，本次一并修复。
            //
            // 注意：ReadOverrideRootAsync 的 Deserialize<Dictionary<string, object>> 产物中，
            // 嵌套节点是 JsonElement 而非 Dictionary<string, object>——用 `is Dictionary<string, object>`
            // 模式匹配恒不命中，必须按 JsonValueKind.Object 枚举拷贝，否则合并退化为覆盖。
            var notif = new Dictionary<string, object>();
            if (root.TryGetValue("Notification", out var existingNode)
                && existingNode is JsonElement { ValueKind: JsonValueKind.Object } existingObj)
            {
                foreach (var prop in existingObj.EnumerateObject())
                {
                    notif[prop.Name] = prop.Value; // JsonElement 值原样保留，序列化时按原样写出
                }
            }

            notif["DingTalk"] = new Dictionary<string, object>
            {
                ["Enabled"] = merged.DingTalk.Enabled,
                ["Webhook"] = merged.DingTalk.Webhook,
                ["Secret"] = merged.DingTalk.Secret
            };
            notif["Email"] = new Dictionary<string, object>
            {
                ["Enabled"] = merged.Email.Enabled,
                ["SmtpHost"] = merged.Email.SmtpHost,
                ["SmtpPort"] = merged.Email.SmtpPort,
                ["UseSsl"] = merged.Email.UseSsl,
                ["Username"] = merged.Email.Username,
                ["Password"] = merged.Email.Password,
                ["From"] = merged.Email.From,
                ["FromName"] = merged.Email.FromName,
                ["To"] = merged.Email.To
            };
            notif["WeCom"] = new Dictionary<string, object>
            {
                ["Enabled"] = merged.WeCom.Enabled,
                ["Webhook"] = merged.WeCom.Webhook
            };
            notif["Push"] = SerializePush(merged.Push);
            notif["Templates"] = SerializeTemplates(merged.Templates);
            root["Notification"] = notif;

            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(path, json);
            _logger.LogInformation("通知配置已写入 override 文件：{Path}（重启后生效）。", path);
        }

        /// <inheritdoc/>
        public async Task<NotificationTestResult> TestDingTalkAsync(DingTalkConfigDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Webhook))
            {
                return new NotificationTestResult { Success = false, Message = "请先填写 Webhook 地址。" };
            }

            var opts = Options.Create(new NotificationOptions
            {
                DingTalk = new DingTalkOptions
                {
                    Enabled = true,
                    Webhook = dto.Webhook.Trim(),
                    Secret = ResolveSecret(dto.Secret, _current.CurrentValue.DingTalk.Secret, dto.HasSecret)
                }
            });
            var sender = new DingTalkRobotClient(_httpClientFactory, opts, _loggerFactory.CreateLogger<DingTalkRobotClient>());

            return await SendTestAsync(sender, "钉钉", s => s.SendAsync(
                new ExternalMessage
                {
                    Category = ExternalMessageCategory.SystemError,
                    Title = "SCADA 通知测试",
                    MarkdownText = $"## SCADA 通知测试\n- 来源：通知中心测试发送\n- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                }, CancellationToken.None));
        }

        /// <inheritdoc/>
        public async Task<NotificationTestResult> TestEmailAsync(EmailConfigDto dto)
        {
            if (dto == null ||
                string.IsNullOrWhiteSpace(dto.SmtpHost) ||
                string.IsNullOrWhiteSpace(dto.Username) ||
                string.IsNullOrWhiteSpace(dto.From) ||
                dto.To == null || dto.To.Count == 0)
            {
                return new NotificationTestResult { Success = false, Message = "请填写 SMTP 主机/账号/发件人/收件人后再测试。" };
            }

            var opts = Options.Create(new NotificationOptions
            {
                Email = new EmailOptions
                {
                    Enabled = true,
                    SmtpHost = dto.SmtpHost.Trim(),
                    SmtpPort = dto.SmtpPort <= 0 ? 465 : dto.SmtpPort,
                    UseSsl = dto.UseSsl,
                    Username = dto.Username.Trim(),
                    Password = ResolveSecret(dto.Password, _current.CurrentValue.Email.Password, dto.HasPassword),
                    From = dto.From.Trim(),
                    FromName = string.IsNullOrWhiteSpace(dto.FromName) ? "SCADA 报警中心" : dto.FromName.Trim(),
                    To = dto.To.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList()
                }
            });
            var sender = new EmailSender(opts, _loggerFactory.CreateLogger<EmailSender>());

            return await SendTestAsync(sender, "邮件", s => s.SendAsync(
                new ExternalMessage
                {
                    Category = ExternalMessageCategory.SystemError,
                    Title = "SCADA 通知测试",
                    MarkdownText = $"## SCADA 通知测试\n- 来源：通知中心测试发送\n- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    HtmlBody = $"<h3>SCADA 通知测试</h3><p>来源：通知中心测试发送<br/>时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>"
                }, CancellationToken.None));
        }

        /// <inheritdoc/>
        public async Task<NotificationTestResult> TestWeComAsync(WeComConfigDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Webhook))
            {
                return new NotificationTestResult { Success = false, Message = "请先填写 Webhook 地址。" };
            }

            var opts = Options.Create(new NotificationOptions
            {
                WeCom = new WeComOptions
                {
                    Enabled = true,
                    Webhook = dto.Webhook.Trim()
                }
            });
            var sender = new WeComRobotClient(_httpClientFactory, opts, _loggerFactory.CreateLogger<WeComRobotClient>());

            return await SendTestAsync(sender, "企业微信", s => s.SendAsync(
                new ExternalMessage
                {
                    Category = ExternalMessageCategory.SystemError,
                    Title = "SCADA 通知测试",
                    MarkdownText = $"## SCADA 通知测试\n- 来源：通知中心测试发送\n- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                }, CancellationToken.None));
        }

        // ===== helpers =====

        private async Task<NotificationTestResult> SendTestAsync(
            IExternalMessageSender sender, string channel, Func<IExternalMessageSender, Task> send)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await send(sender);
                sw.Stop();
                RecordTestOutcome(channel, sender, "Success", sw.ElapsedMilliseconds, null);
                return new NotificationTestResult { Success = true, LatencyMs = sw.ElapsedMilliseconds, Message = $"{channel} 测试发送成功。" };
            }
            catch (Exception ex)
            {
                sw.Stop();
                RecordTestOutcome(channel, sender, "Failed", sw.ElapsedMilliseconds, ex.Message);
                return new NotificationTestResult { Success = false, LatencyMs = sw.ElapsedMilliseconds, Message = $"{channel} 测试发送失败：{ex.Message}" };
            }
        }

        /// <summary>测试发送落投递记录（EventType=test；用临时配置发送，PayloadJson 为空、不可重试）。</summary>
        private void RecordTestOutcome(string channel, IExternalMessageSender sender, string status, long latencyMs, string? error)
        {
            try
            {
                _logRecorder.Record(new NotificationLogEntry(
                    Channel: channel switch
                    {
                        "钉钉" => "dingTalk",
                        "邮件" => "email",
                        "企业微信" => "weCom",
                        _ => channel.ToLowerInvariant()
                    },
                    EventType: "test",
                    Title: "SCADA 通知测试",
                    Recipient: sender.RecipientSummary,
                    Status: status,
                    LatencyMs: latencyMs,
                    Error: error,
                    PayloadPreview: "## SCADA 通知测试\n- 来源：通知中心测试发送",
                    PayloadJson: null,
                    SourceLogId: null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "测试发送投递记录埋点异常（不影响测试流程）。");
            }
        }

        /// <summary>掩码/空且标记已有 => 沿用旧值；否则用提交值（新密码明文）。</summary>
        private static string ResolveSecret(string submitted, string existing, bool hasExisting)
            => (string.IsNullOrEmpty(submitted) || submitted == SecretMask) && hasExisting
                ? existing
                : (submitted ?? string.Empty).Trim();

        private static bool TryParseAddress(string address) =>
            MimeKit.MailboxAddress.TryParse(address, out _);

        private static Dictionary<string, string> SerializePush(ExternalPushPolicy p) => new Dictionary<string, string?>
        {
            ["PushAlarm"] = p.PushAlarm.ToString(),
            ["PushDeviceOffline"] = p.PushDeviceOffline.ToString(),
            ["PushDeviceOnline"] = p.PushDeviceOnline.ToString(),
            ["DeviceStatusDebounceMinutes"] = p.DeviceStatusDebounceMinutes.ToString(),
            ["PushSystemAlarm"] = p.PushSystemAlarm.ToString(),
            ["PushSystemError"] = p.PushSystemError.ToString(),
            ["PushScript"] = p.PushScript.ToString(),
            ["MaxPerMinutePerChannel"] = p.MaxPerMinutePerChannel.ToString(),
            ["MaxAttempts"] = p.MaxAttempts.ToString(),
            ["RetryBaseDelayMs"] = p.RetryBaseDelayMs.ToString(),
            ["QueueCapacity"] = p.QueueCapacity.ToString()
        }.ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

        private static Dictionary<string, object> SerializeTemplates(NotificationTemplates t)
            => JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(t))
               ?? new Dictionary<string, object>();

        private async Task<Dictionary<string, object>> ReadOverrideRootAsync()
        {
            var path = GetOverridePath();
            if (!System.IO.File.Exists(path))
            {
                return new Dictionary<string, object>();
            }
            try
            {
                var text = await System.IO.File.ReadAllTextAsync(path);
                var root = JsonSerializer.Deserialize<Dictionary<string, object>>(text);
                return root ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取 override 文件失败，将基于空配置写入：{Path}", path);
                return new Dictionary<string, object>();
            }
        }

        private string GetOverridePath() =>
            System.IO.Path.Combine(_env.ContentRootPath, OverrideFileName);
    }
}
