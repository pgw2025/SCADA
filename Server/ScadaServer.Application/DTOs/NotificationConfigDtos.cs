using System.Collections.Generic;
using ScadaServer.Application.Options;

namespace ScadaServer.Application.DTOs
{
    /// <summary>消息通知配置（GET/PUT 使用；敏感字段回显掩码，配合 HasXxx 判断是否修改）。</summary>
    public class NotificationConfigDto
    {
        public DingTalkConfigDto DingTalk { get; set; } = new();
        public EmailConfigDto Email { get; set; } = new();
        /// <summary>企业微信配置片段。可空无初始化器：请求体缺失该字段时绑定为 null，
        /// SaveAsync 据此沿用旧值——若用 = new() 初始化，旧客户端（PWA 离线壳缓存的旧 JS 包）
        /// PUT 不带 weCom 字段时会绑定到空实例（Enabled=false），已启用渠道被静默重置。</summary>
        public WeComConfigDto? WeCom { get; set; }
        public ExternalPushPolicy Push { get; set; } = new();
        public NotificationTemplates Templates { get; set; } = new();
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

    /// <summary>企业微信群机器人配置片段（webhook 型，无加签字段）。</summary>
    public class WeComConfigDto
    {
        public bool Enabled { get; set; }
        public string Webhook { get; set; } = string.Empty;
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

    /// <summary>
    /// 投递记录条目（GET /logs 与重试接口返回，与前端 NotificationLogItem 字段一一对应，
    /// JSON camelCase 序列化自动对齐）。
    /// </summary>
    public class NotificationLogDto
    {
        public long Id { get; set; }

        /// <summary>投递完成时间（本地时区 ISO 字符串，前端直接展示）。</summary>
        public string Timestamp { get; set; } = string.Empty;

        /// <summary>渠道标识：dingTalk | weCom | email | webPush</summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>事件类型：alarmTriggered | alarmRecovered | deviceStatus | systemAlarm | systemError | scriptExecution | test</summary>
        public string EventType { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        /// <summary>收件方摘要</summary>
        public string Recipient { get; set; } = string.Empty;

        /// <summary>Success | Failed | Retrying</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>投递耗时（毫秒，含重试退避的总耗时，即端到端完成时间）</summary>
        public long LatencyMs { get; set; }

        public string? Error { get; set; }

        /// <summary>正文预览（MarkdownText 截断）</summary>
        public string? PayloadPreview { get; set; }
    }
}
