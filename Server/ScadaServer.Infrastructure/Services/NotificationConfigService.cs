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

        private readonly IOptions<NotificationOptions> _current;
        private readonly IHostEnvironment _env;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<NotificationConfigService> _logger;

        public NotificationConfigService(
            IOptions<NotificationOptions> current,
            IHostEnvironment env,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            ILogger<NotificationConfigService> logger)
        {
            _current = current;
            _env = env;
            _httpClientFactory = httpClientFactory;
            _loggerFactory = loggerFactory;
            _logger = logger;
        }

        /// <inheritdoc/>
        public Task<NotificationConfigDto> GetAsync()
        {
            var o = _current.Value;
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
                Push = o.Push
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
            var current = _current.Value;
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
                // 完整保留 Push 策略（前端回传整段对象，含未编辑的高级参数）
                Push = dto.Push ?? current.Push
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

            var urlPrefix = _current.Value; // 仅占位避免告警，实际不读取
            var payload = new Dictionary<string, object>
            {
                ["Notification"] = new Dictionary<string, object>
                {
                    ["DingTalk"] = new Dictionary<string, object>
                    {
                        ["Enabled"] = merged.DingTalk.Enabled,
                        ["Webhook"] = merged.DingTalk.Webhook,
                        ["Secret"] = merged.DingTalk.Secret
                    },
                    ["Email"] = new Dictionary<string, object>
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
                    },
                    ["Push"] = SerializePush(merged.Push)
                }
            };

            var path = GetOverridePath();
            var root = await ReadOverrideRootAsync();
            root["Notification"] = payload["Notification"];
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
                    Secret = ResolveSecret(dto.Secret, _current.Value.DingTalk.Secret, dto.HasSecret)
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
                    Password = ResolveSecret(dto.Password, _current.Value.Email.Password, dto.HasPassword),
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

        // ===== helpers =====

        private async Task<NotificationTestResult> SendTestAsync(
            IExternalMessageSender sender, string channel, Func<IExternalMessageSender, Task> send)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await send(sender);
                sw.Stop();
                return new NotificationTestResult { Success = true, LatencyMs = sw.ElapsedMilliseconds, Message = $"{channel} 测试发送成功。" };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new NotificationTestResult { Success = false, LatencyMs = sw.ElapsedMilliseconds, Message = $"{channel} 测试发送失败：{ex.Message}" };
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
