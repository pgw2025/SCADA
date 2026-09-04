using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>SMTP 邮件发送器（MailKit）。邮件正文由调用方全量转义后传入，防模板注入。</summary>
    public class EmailSender : IExternalMessageSender
    {
        /// <summary>单次 SMTP 操作超时（MailKit 默认约 100s，显式收敛与钉钉渠道对齐）。</summary>
        private const int SmtpTimeoutMs = 8000;

        private readonly EmailOptions _options;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IOptions<NotificationOptions> options, ILogger<EmailSender> logger)
        {
            _options = options.Value.Email;
            _logger = logger;
        }

        public string Name => "Email";

        public bool Enabled => _options.Enabled
            && !string.IsNullOrWhiteSpace(_options.SmtpHost)
            && !string.IsNullOrWhiteSpace(_options.Username)
            && !string.IsNullOrWhiteSpace(_options.Password)
            && !string.IsNullOrWhiteSpace(_options.From)
            && _options.To.Count > 0;

        public async Task SendAsync(ExternalMessage message, CancellationToken cancellationToken)
        {
            using var mail = new MimeMessage();
            mail.From.Add(new MailboxAddress(_options.FromName, _options.From));
            foreach (var to in _options.To)
            {
                if (!string.IsNullOrWhiteSpace(to))
                {
                    mail.To.Add(MailboxAddress.Parse(to.Trim()));
                }
            }
            mail.Subject = message.Title;
            mail.Body = new TextPart(TextFormat.Html)
            {
                Text = message.HtmlBody ?? MarkdownToHtml(message.MarkdownText)
            };

            using var client = new SmtpClient { Timeout = SmtpTimeoutMs };
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, _options.UseSsl, cancellationToken);
            try
            {
                await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
                await client.SendAsync(mail, cancellationToken);
            }
            finally
            {
                try
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 停机取消：连接已无意义，忽略断开失败
                }
            }
        }

        /// <summary>兜底 HTML 化：逐行转义 + 换行转 br（调用方提供 HtmlBody 时不走此路径）。</summary>
        private static string MarkdownToHtml(string markdown)
            => string.Join("<br/>", markdown.Split('\n').Select(WebUtility.HtmlEncode));
    }
}
