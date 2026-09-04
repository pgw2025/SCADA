using System.Collections.Generic;
using ScadaServer.Application.Options;

namespace ScadaServer.Application.DTOs
{
    /// <summary>消息通知配置（GET/PUT 使用；敏感字段回显掩码，配合 HasXxx 判断是否修改）。</summary>
    public class NotificationConfigDto
    {
        public DingTalkConfigDto DingTalk { get; set; } = new();
        public EmailConfigDto Email { get; set; } = new();
        public ExternalPushPolicy Push { get; set; } = new();
    }

    /// <summary>钉钉群机器人配置片段。</summary>
    public class DingTalkConfigDto
    {
        public bool Enabled { get; set; }
        public string Webhook { get; set; } = string.Empty;
        /// <summary>加签密钥；GET 回显时以掩码占位。</summary>
        public string Secret { get; set; } = string.Empty;
        /// <summary>是否存在已配置的加签密钥。</summary>
        public bool HasSecret { get; set; }
    }

    /// <summary>SMTP 邮件配置片段。</summary>
    public class EmailConfigDto
    {
        public bool Enabled { get; set; }
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 465;
        public bool UseSsl { get; set; } = true;
        public string Username { get; set; } = string.Empty;
        /// <summary>SMTP 授权码；GET 回显时以掩码占位。</summary>
        public string Password { get; set; } = string.Empty;
        public bool HasPassword { get; set; }
        public string From { get; set; } = string.Empty;
        public string FromName { get; set; } = "SCADA 报警中心";
        public List<string> To { get; set; } = new();
    }

    /// <summary>测试发送结果。</summary>
    public class NotificationTestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long? LatencyMs { get; set; }
    }
}
