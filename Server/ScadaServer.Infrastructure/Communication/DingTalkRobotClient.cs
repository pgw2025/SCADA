using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>钉钉群机器人发送器（markdown 消息，支持加签安全模式）。</summary>
    public class DingTalkRobotClient : IExternalMessageSender
    {
        /// <summary>命名 HttpClient 注册名（WebApi 注册时配置 8s 超时，替代默认 100s）。</summary>
        public const string HttpClientName = "DingTalk";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DingTalkOptions _options;
        private readonly ILogger<DingTalkRobotClient> _logger;

        public DingTalkRobotClient(
            IHttpClientFactory httpClientFactory,
            IOptions<NotificationOptions> options,
            ILogger<DingTalkRobotClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value.DingTalk;
            _logger = logger;
        }

        public string Name => "DingTalk";

        public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Webhook);

        public async Task SendAsync(ExternalMessage message, CancellationToken cancellationToken)
        {
            var url = _options.Webhook;

            // 加签：timestamp + "\n" + secret 的 HMAC-SHA256 -> Base64 -> UrlEncode。
            if (!string.IsNullOrWhiteSpace(_options.Secret))
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var stringToSign = timestamp + "\n" + _options.Secret;
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.Secret));
                var sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                var separator = url.Contains('?') ? '&' : '?';
                url = $"{url}{separator}timestamp={timestamp}&sign={WebUtility.UrlEncode(sign)}";
            }

            var payload = new
            {
                msgtype = "markdown",
                markdown = new { title = message.Title, text = message.MarkdownText }
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            // 钉钉业务失败（关键词不匹配/限流）也返回 HTTP 200，必须解析 errcode。
            var result = JsonSerializer.Deserialize<DingTalkResponse>(body);
            if (result?.Errcode != 0)
            {
                throw new InvalidOperationException($"钉钉机器人拒绝消息（errcode={result?.Errcode}）：{result?.Errmsg ?? body}");
            }
        }

        private sealed class DingTalkResponse
        {
            [JsonPropertyName("errcode")]
            public int Errcode { get; set; }

            [JsonPropertyName("errmsg")]
            public string? Errmsg { get; set; }
        }
    }
}
