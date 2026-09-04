namespace ScadaServer.Application.Options
{
    public class NotificationOptions
    {
        public const string SectionName = "Notification";

        public DingTalkOptions DingTalk { get; set; } = new();
        public EmailOptions Email { get; set; } = new();
        public ExternalPushPolicy Push { get; set; } = new();
        public NotificationTemplates Templates { get; set; } = new();
    }

    /// <summary>可配置的外部消息模板（标记/钉钉/邮件各租一套占位符文案），未覆盖时用默认值。</summary>
    public class NotificationTemplates
    {
        public EventTemplate AlarmTriggered { get; set; } = EventTemplate.AlarmTriggeredDefault();
        public EventTemplate AlarmRecovered { get; set; } = EventTemplate.AlarmRecoveredDefault();
        public EventTemplate DeviceStatus { get; set; } = EventTemplate.DeviceStatusDefault();
        public EventTemplate SystemAlarm { get; set; } = EventTemplate.SystemAlarmDefault();
        public EventTemplate SystemError { get; set; } = EventTemplate.SystemErrorDefault();
        public EventTemplate ScriptExecution { get; set; } = EventTemplate.ScriptExecutionDefault();
    }

    /// <summary>单个事件的模板：标题 + 钉钉 Markdown 正文 + 邮件 HTML 正文，均支持 {占位符}。</summary>
    public class EventTemplate
    {
        public string Title { get; set; } = string.Empty;
        public string Markdown { get; set; } = string.Empty;
        public string HtmlBody { get; set; } = string.Empty;

        /// <summary>把 <paramref name="configured"/> 与 <paramref name="fallback"/> 逐字段合并：</summary>
        /// <remarks>配置字段为空/空白时回退到系统默认（承载“留空=沿用默认”的前端约定）。</remarks>
        public static EventTemplate Merge(EventTemplate configured, EventTemplate fallback) => new()
        {
            Title = string.IsNullOrWhiteSpace(configured.Title) ? fallback.Title : configured.Title,
            Markdown = string.IsNullOrWhiteSpace(configured.Markdown) ? fallback.Markdown : configured.Markdown,
            HtmlBody = string.IsNullOrWhiteSpace(configured.HtmlBody) ? fallback.HtmlBody : configured.HtmlBody
        };

        public static EventTemplate AlarmTriggeredDefault() => new()
        {
            Title = "⚠️ 报警触发 [{variableName}]",
            Markdown = "## ⚠️ 报警触发\n"
                + "- 设备：{deviceKey}（{deviceId}）\n"
                + "- 变量：{variableName}（{variableKey}）\n"
                + "- 规则：{ruleName}\n"
                + "- 级别：**{level}**\n"
                + "- 条件：{condition}  阈值：{threshold}\n"
                + "- 实际值：{actualValue}\n"
                + "- 来源：{source}\n"
                + "- 时间：{time}",
            HtmlBody = "<h3>⚠️ 报警触发</h3>"
                + "<table cellpadding='4' border='1'>"
                + "<tr><th>设备</th><th>变量</th><th>规则</th><th>级别</th><th>条件/阈值</th><th>实际值</th><th>时间</th></tr>"
                + "<tr><td>{deviceKey}(#{deviceId})</td>"
                + "<td>{variableName}</td><td>{ruleName}</td><td>{level}</td>"
                + "<td>{condition} / {threshold}</td><td>{actualValue}</td><td>{time}</td></tr></table>"
                + "<p>{message}</p>"
        };

        public static EventTemplate AlarmRecoveredDefault() => new()
        {
            Title = "✅ 报警恢复 [{variableName}]",
            Markdown = "## ✅ 报警恢复\n"
                + "- 设备：{deviceKey}（{deviceId}）\n"
                + "- 变量：{variableName}（{variableKey}）\n"
                + "- 规则：{ruleName}\n"
                + "- 级别：**{level}**\n"
                + "- 条件：{condition}  阈值：{threshold}\n"
                + "- 实际值：{actualValue}\n"
                + "- 来源：{source}\n"
                + "- 时间：{time}",
            HtmlBody = "<h3>✅ 报警恢复</h3>"
                + "<table cellpadding='4' border='1'>"
                + "<tr><th>设备</th><th>变量</th><th>规则</th><th>级别</th><th>条件/阈值</th><th>实际值</th><th>时间</th></tr>"
                + "<tr><td>{deviceKey}(#{deviceId})</td>"
                + "<td>{variableName}</td><td>{ruleName}</td><td>{level}</td>"
                + "<td>{condition} / {threshold}</td><td>{actualValue}</td><td>{time}</td></tr></table>"
                + "<p>{message}</p>"
        };

        public static EventTemplate DeviceStatusDefault() => new()
        {
            Title = "设备{status} [#{deviceId}]",
            Markdown = "## SCADA 设备状态变更\n"
                + "- 设备ID：{deviceId}\n"
                + "- 状态：**{status}**\n"
                + "- 时间：{time}",
            HtmlBody = "<h3>SCADA 设备状态变更</h3>"
                + "<p>设备ID：{deviceId}<br/>状态：<b>{status}</b><br/>时间：{time}</p>"
        };

        public static EventTemplate SystemAlarmDefault() => new()
        {
            Title = "系统报警 [{variableName}]",
            Markdown = "## 系统报警\n"
                + "- 设备ID：{deviceId}\n"
                + "- 变量：{variableName}（{variableKey}）\n"
                + "- 级别：**{level}**\n"
                + "- 内容：{message}\n"
                + "- 时间：{time}",
            HtmlBody = "<h3>系统报警</h3>"
                + "<p>设备ID：{deviceId}<br/>变量：{variableName}（{variableKey}）<br/>"
                + "级别：{level}<br/>内容：{message}</p>"
        };

        public static EventTemplate SystemErrorDefault() => new()
        {
            Title = "[{level}] 系统异常 {source}",
            Markdown = "## 系统异常（{level}）\n"
                + "- 来源：{source}\n"
                + "- 时间：{time}\n\n{content}",
            HtmlBody = "<h3>系统异常（{level}）</h3>"
                + "<p>来源：{source}　时间：{time}</p>"
                + "<pre>{content}</pre>"
        };

        public static EventTemplate ScriptExecutionDefault() => new()
        {
            Title = "脚本执行异常 [#{scriptId} {result}]",
            Markdown = "## 脚本执行异常\n"
                + "- 脚本ID：{scriptId}（v{scriptVersion}）\n"
                + "- 触发：{triggerSource}\n"
                + "- 结果：**{result}**\n"
                + "- 错误：{error}\n"
                + "- 耗时：{durationMs}ms\n"
                + "- 时间：{time}",
            HtmlBody = "<h3>脚本执行异常</h3>"
                + "<p>脚本ID：{scriptId}（v{scriptVersion}）　触发：{triggerSource}"
                + "　结果：<b>{result}</b>　耗时：{durationMs}ms　时间：{time}</p>"
                + "<pre>{error}</pre>"
        };
    }

    public class DingTalkOptions
    {
        public bool Enabled { get; set; }
        public string Webhook { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
    }

    public class EmailOptions
    {
        public bool Enabled { get; set; }
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 465;
        public bool UseSsl { get; set; } = true;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string FromName { get; set; } = "SCADA 报警中心";
        public List<string> To { get; set; } = new();
    }

    public class ExternalPushPolicy
    {
        public bool PushAlarm { get; set; } = true;
        public bool PushDeviceOffline { get; set; } = true;
        public bool PushDeviceOnline { get; set; } = false;
        public int DeviceStatusDebounceMinutes { get; set; } = 5;
        public bool PushSystemAlarm { get; set; } = true;
        public bool PushSystemError { get; set; } = true;
        public bool PushScript { get; set; } = true;
        public int MaxPerMinutePerChannel { get; set; } = 15;
        public int MaxAttempts { get; set; } = 2;
        public int RetryBaseDelayMs { get; set; } = 1000;
        public int QueueCapacity { get; set; } = 2048;
    }
}
