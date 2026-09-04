using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Application.Services;
using ScadaServer.Domain.Enums;

namespace ScadaServer.WebApi.Services
{
    /// <summary>
    /// 外部消息通知装饰器：包裹原 SignalR 通知服务（前端/采集推送路径完全不变），
    /// 按策略把可外发事件用可配置模板格式化后非阻塞入队（钉钉/邮件后台分发）。
    /// <para>时间展示统一转本地时区（事件时间按项目约定为 UTC）；邮件 HTML 全量转义防注入。</para>
    /// </summary>
    public class ExternalNotificationDecorator : IScadaNotificationService
    {
        private readonly IScadaNotificationService _inner;
        private readonly IExternalNotificationQueue _queue;
        private readonly ExternalPushPolicy _policy;
        private readonly NotificationTemplates _templates;
        private readonly NotificationTemplateEngine _engine;
        private readonly ILogger<ExternalNotificationDecorator> _logger;
        private readonly ConcurrentDictionary<int, DateTime> _lastDeviceStatusPushUtc = new();

        public ExternalNotificationDecorator(
            IScadaNotificationService inner,
            IExternalNotificationQueue queue,
            IOptions<NotificationOptions> options,
            NotificationTemplateEngine engine,
            ILogger<ExternalNotificationDecorator> logger)
        {
            _inner = inner;
            _queue = queue;
            _policy = options.Value.Push;
            _templates = options.Value.Templates;
            _engine = engine;
            _logger = logger;
        }

        /// <inheritdoc/>
        // 变量更新不外发（高频噪音），直接透传。
        public Task NotifyVariableUpdateAsync(int deviceId, string variableKey, object? value, VariableQuality quality, DateTime updateTime)
            => _inner.NotifyVariableUpdateAsync(deviceId, variableKey, value, quality, updateTime);

        /// <inheritdoc/>
        public async Task NotifyDeviceStatusAsync(int deviceId, DeviceStatus status)
        {
            await _inner.NotifyDeviceStatusAsync(deviceId, status);

            if (!_queue.HasEnabledChannels) return;

            // Offline/Fault 外发（故障必发）；Online 默认不发；ConfigUpdating/Connecting 状态噪音不外发。
            var push = status switch
            {
                DeviceStatus.Offline => _policy.PushDeviceOffline,
                DeviceStatus.Online => _policy.PushDeviceOnline,
                DeviceStatus.Fault => true,
                _ => false
            };
            if (!push) return;

            // 去抖：同一设备窗口内只外发一次（重连风暴防护）。
            var debounce = TimeSpan.FromMinutes(Math.Max(0, _policy.DeviceStatusDebounceMinutes));
            if (debounce > TimeSpan.Zero)
            {
                var now = DateTime.UtcNow;
                if (_lastDeviceStatusPushUtc.TryGetValue(deviceId, out var last) && now - last < debounce)
                {
                    return;
                }
                _lastDeviceStatusPushUtc[deviceId] = now;
            }

            var template = EventTemplate.Merge(_templates.DeviceStatus, EventTemplate.DeviceStatusDefault());
            var tokens = new Dictionary<string, string?>
            {
                { "status", status.ToString() },
                { "deviceId", deviceId.ToString() },
                { "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            _queue.Enqueue(new ExternalMessage
            {
                Category = ExternalMessageCategory.DeviceStatus,
                Title = _engine.Render(template.Title, tokens),
                MarkdownText = _engine.Render(template.Markdown, tokens),
                HtmlBody = _engine.Render(template.HtmlBody, tokens, htmlEncode: true)
            });
        }

        /// <inheritdoc/>
        public async Task NotifySystemAlarmAsync(int deviceId, string variableKey, string variableName, string message, string level)
        {
            await _inner.NotifySystemAlarmAsync(deviceId, variableKey, variableName, message, level);

            if (!_queue.HasEnabledChannels || !_policy.PushSystemAlarm) return;

            var template = EventTemplate.Merge(_templates.SystemAlarm, EventTemplate.SystemAlarmDefault());
            var tokens = new Dictionary<string, string?>
            {
                { "deviceId", deviceId.ToString() },
                { "variableName", variableName },
                { "variableKey", variableKey },
                { "level", level },
                { "message", message },
                { "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            _queue.Enqueue(new ExternalMessage
            {
                Category = ExternalMessageCategory.SystemAlarm,
                Title = _engine.Render(template.Title, tokens),
                MarkdownText = _engine.Render(template.Markdown, tokens),
                HtmlBody = _engine.Render(template.HtmlBody, tokens, htmlEncode: true)
            });
        }

        /// <inheritdoc/>
        public async Task NotifyAlarmAsync(AlarmEvent evt)
        {
            await _inner.NotifyAlarmAsync(evt);

            if (!_queue.HasEnabledChannels || !_policy.PushAlarm) return;

            var template = evt.EventType == AlarmEventType.Triggered
                ? EventTemplate.Merge(_templates.AlarmTriggered, EventTemplate.AlarmTriggeredDefault())
                : EventTemplate.Merge(_templates.AlarmRecovered, EventTemplate.AlarmRecoveredDefault());
            var localTime = ToLocalDisplay(evt.TriggeredAt); // UTC -> 本地时区展示
            var tokens = new Dictionary<string, string?>
            {
                { "deviceKey", evt.DeviceKey },
                { "deviceId", evt.DeviceId.ToString() },
                { "variableKey", evt.VariableKey },
                { "variableName", evt.VariableName },
                { "ruleName", evt.RuleName ?? "-" },
                { "level", evt.Level.ToString() },
                { "condition", evt.Condition?.ToString() ?? "-" },
                { "threshold", evt.Threshold?.ToString() ?? "-" },
                { "actualValue", evt.ActualValue ?? "-" },
                { "source", evt.Source.ToString() },
                { "message", evt.Message },
                { "time", localTime.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            _queue.Enqueue(new ExternalMessage
            {
                Category = ExternalMessageCategory.Alarm,
                Title = _engine.Render(template.Title, tokens),
                MarkdownText = _engine.Render(template.Markdown, tokens),
                HtmlBody = _engine.Render(template.HtmlBody, tokens, htmlEncode: true)
            });
        }

        /// <inheritdoc/>
        public async Task NotifyScriptExecutionAsync(ScriptExecutionEvent evt)
        {
            await _inner.NotifyScriptExecutionAsync(evt);

            // 仅外发非 Success 结果（Success 每次都发会刷屏）。
            if (!_queue.HasEnabledChannels || !_policy.PushScript || evt.Result == "Success") return;

            var localTime = ToLocalDisplay(evt.StartedAt); // UTC -> 本地时区展示
            var template = EventTemplate.Merge(_templates.ScriptExecution, EventTemplate.ScriptExecutionDefault());
            var tokens = new Dictionary<string, string?>
            {
                { "scriptId", evt.ScriptId.ToString() },
                { "scriptVersion", evt.ScriptVersion.ToString() },
                { "triggerSource", evt.TriggerSource },
                { "result", evt.Result },
                { "error", evt.Error ?? "-" },
                { "durationMs", evt.DurationMs.ToString() },
                { "time", localTime.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            _queue.Enqueue(new ExternalMessage
            {
                Category = ExternalMessageCategory.ScriptExecution,
                Title = _engine.Render(template.Title, tokens),
                MarkdownText = _engine.Render(template.Markdown, tokens),
                HtmlBody = _engine.Render(template.HtmlBody, tokens, htmlEncode: true)
            });
        }

        /// <summary>UTC -> 本地时区展示（项目约定事件时间为 UTC；未标注 Kind 的按 UTC 处理）。</summary>
        private static DateTime ToLocalDisplay(DateTime time)
        {
            var utc = time.Kind switch
            {
                DateTimeKind.Local => time.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
                _ => time
            };
            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
        }
    }
}