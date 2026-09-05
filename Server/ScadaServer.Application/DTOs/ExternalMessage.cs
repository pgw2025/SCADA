namespace ScadaServer.Application.DTOs
{
    public enum ExternalMessageCategory
    {
        Alarm,
        DeviceStatus,
        SystemAlarm,
        SystemError,
        ScriptExecution
    }

    public class ExternalMessage
    {
        public ExternalMessageCategory Category { get; set; }
        public string Title { get; set; } = string.Empty;
        public string MarkdownText { get; set; } = string.Empty;
        public string? HtmlBody { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // ---- Web Push 渠道专用可选字段（doc/pwa 阶段五 D8，向后兼容：钉钉/邮件不读不写）----

        /// <summary>事件级别（Alarm=报警级别；SystemError=Critical…），Web Push 级别过滤用</summary>
        public string? Severity { get; set; }

        /// <summary>模板占位符上下文（deviceKey/variableName/level/actualValue/time/eventType…），
        /// Web Push 据此构造结构化 payload（正文摘要/tag 折叠/恢复过滤）</summary>
        public Dictionary<string, string?>? Tokens { get; set; }

        // ---- 投递记录/重试专用可选字段（消息通知中心 · 投递记录功能，向后兼容：null = 正常事件消息）----

        /// <summary>定向渠道（Sender.Name）：重试消息只投递到该渠道；null = 扇出到全部启用渠道</summary>
        public string? TargetChannel { get; set; }

        /// <summary>来源投递记录 Id：重试路径携带，管线完成后更新原行而非新增行</summary>
        public long? SourceLogId { get; set; }
    }
}
