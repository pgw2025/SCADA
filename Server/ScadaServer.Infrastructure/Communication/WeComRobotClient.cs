using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>企业微信群机器人发送器（markdown 消息，webhook 型，无加签）。</summary>
    /// <remarks>
    /// 作为第四渠道接入既有通知管线（与钉钉/邮件/Web Push 并列），自动继承限流/重试/投递记录治理。
    /// 协议差异：
    ///   1) markdown 无独立 title 字段，仅 content；Title 前置为加粗标题。
    ///   2) 无 HMAC 加签，安全靠机器人「自定义关键词」+「IP 白名单」。
    ///   3) content ≤ 4096 字节（UTF-8），超出按字节安全截断并追加提示后缀。
    ///   4) font 标签只支持 warning|info|comment 枚举色：hex 色标签对剥除保留文本，
    ///      枚举色标签对合法原样保留（用户自定义模板可正常使用）。
    /// </remarks>
    public class WeComRobotClient : IExternalMessageSender
    {
        /// <summary>命名 HttpClient 注册名（WebApi 注册时配置 8s 超时，同钉钉）。</summary>
        public const string HttpClientName = "WeCom";

        private const int MaxContentBytes = 4096;

        /// <summary>超长截断提示（追加后总字节数仍 ≤ 4096）。</summary>
        private const string TruncateNotice = "\n\n（内容超长已截断）";

        /// <summary>hex 色 font 标签对（闭合标签一起剥除，保留内部文本）。
        /// 只匹配 color="#xxxxxx" 形式；枚举色（warning|info|comment）不命中、原样保留。
        /// Singleline：兼容标签内含换行的正文。</summary>
        private static readonly Regex HexFontRegex = new(
            @"<font\s[^>]*color=[""']#[0-9a-fA-F]{3,8}[""'][^>]*>(.*?)</font>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly WeComOptions _options;
        private readonly ILogger<WeComRobotClient> _logger;

        public WeComRobotClient(
            IHttpClientFactory httpClientFactory,
            IOptions<NotificationOptions> options,
            ILogger<WeComRobotClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value.WeCom;
            _logger = logger;
        }

        public string Name => "WeCom";

        public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Webhook);

        /// <summary>收件方摘要（webhook 含 key，不回显明文）。</summary>
        public string RecipientSummary => "企业微信群机器人";

        public async Task SendAsync(ExternalMessage message, CancellationToken cancellationToken)
        {
            var content = BuildContent(message);

            var payload = new
            {
                msgtype = "markdown",
                markdown = new { content }
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(_options.Webhook, payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            // 企业微信业务失败（关键词不匹配/内容为空/超长）也返回 HTTP 200，必须解析 errcode。
            var result = JsonSerializer.Deserialize<WeComResponse>(body);
            if (result?.Errcode != 0)
            {
                throw new InvalidOperationException($"企业微信机器人拒绝消息（errcode={result?.Errcode}）：{result?.Errmsg ?? body}");
            }
        }

        /// <summary>构造 markdown content：标题前置加粗 → hex 色标签对清洗 → 字节级截断（超限时追加提示，总长仍 ≤ 4096）。</summary>
        private static string BuildContent(ExternalMessage message)
        {
            var body = SanitizeMarkdown($"**{message.Title}**\n\n{message.MarkdownText}");
            var budget = MaxContentBytes - Encoding.UTF8.GetByteCount(TruncateNotice);
            var truncated = TruncateUtf8(body, budget);
            return truncated.Length == body.Length ? body : truncated + TruncateNotice;
        }

        /// <summary>剥除企微不支持的 hex 色 font 标签对（保留内部文本）；枚举色标签对（warning|info|comment）合法保留。</summary>
        private static string SanitizeMarkdown(string? markdown)
            => string.IsNullOrEmpty(markdown)
                ? string.Empty
                : HexFontRegex.Replace(markdown, "$1");

        /// <summary>按 UTF-8 字节数安全截断，回退到上一个合法序列边界，避免截断在多字节字符中间产生乱码。</summary>
        private static string TruncateUtf8(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length <= maxBytes) return value;

            var length = maxBytes;
            while (length > 0 && (bytes[length - 1] & 0xC0) == 0x80)
            {
                length--;
            }
            return Encoding.UTF8.GetString(bytes, 0, length);
        }

        private sealed class WeComResponse
        {
            [JsonPropertyName("errcode")]
            public int Errcode { get; set; }

            [JsonPropertyName("errmsg")]
            public string? Errmsg { get; set; }
        }
    }
}