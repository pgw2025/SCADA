using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Domain.Enums;

namespace ScadaServer.WebApi.Services
{
    /// <summary>
    /// 外部消息通知装饰器：包裹原 SignalR 通知服务（前端/采集推送路径完全不变），
    /// 按策略把可外发事件格式化后非阻塞入队（钉钉/邮件后台分发）。
    /// <para>时间展示统一转本地时区（事件时间按项目约定为 UTC）；邮件 HTML 全量转义防注入。</para>
    /// </summary>
    public class ExternalNotificationDecorator : IScadaNotificationService
    {
        private readonly IScadaNotificationService _inner;
        private readonly IExternalNotificationQueue _queue;
        private readonly ExternalPushPolicy _policy;
        private readonly ILogger<ExternalNotificationDecorator> _logger;
        private readonly ConcurrentDictionary<int, DateTime> _lastDeviceStatusPushUtc = new();

        public ExternalNotificationDecorator(
            IScadaNotificationService inner,
            IExternalNotificationQueue queue,
            IOptions<NotificationOptions> options,
            ILogger<ExternalNotificationDecorator> logger)
        {
            _inner = inner;
            _queue = queue;
            _policy = options.Value.Push;
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

            _queue.Enqueue(new ExternalMessage
            {
                Category = ExternalMessageCategory.DeviceStatus,
                Title = $"设备{status} [#{deviceId}]",
                MarkdownText = $"## SCADA 设备状态变更\n"
                    + $"- 设备ID：{deviceId}\n"
                    + $"- 状态：**{status}**\n"
                    + $"- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            });
        }

        /// <inheritdoc/>
        public async Task NotifySystemAlarmAsync(int deviceId, string variableKey, string variableName, string message, string level)
        {
            await _inner.NotifySystemAlarmAsync(deviceId, variableKey, variableName, message, level);

            if (!_queue.HasEnabledChannels || !_policy.PushSystemAlarm) return;

            _queue.Enqueue(new ExternalMessage
            {
                Category = ExternalMessageCategory.SystemAlarm,
                Title = $"系统报警 [{variableName}]",
                MarkdownText = $"## 系统报警\n"
                    + $"- 设备ID：{deviceId}\n"
                    + $"- 变量：{variableName}（{variableKey}）\n"
                    + $"- 级别：**{level}**\n"
                    + $"- 内容：{message}\n"
                    + $"- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                HtmlBody = "<h3>系统报警</h3>"
                    + $"<p>设备ID：{deviceId}<br/>变量：{WebUtility.HtmlEncode(variableName)}（{WebUtility.HtmlEncode(variableKey)}）<br/>"
                    + $"级别：{WebUtility.HtmlEncode(level)}<br/>内容：{WebUtility.HtmlEncode(message)}</p>"
            });
        }

        /// <inheritdoc/>
        public async Task NotifyAlarmAsync(AlarmEvent evt)
        {
            await _inner.NotifyAlarmAsync(evt);

            if (!_queue.HasEnabledChannels || !_policy.PushAlarm) return;

            var triggered = evt.EventType == AlarmEventType.Triggered;
            var head = triggered ? "⚠️ 报警触发" : "✅ 报警恢复";
            var localTime = ToLocalDisplay(evt.TriggeredAt); // UTC -> 本地时区展示

            _queue.Enqueue(new ExternalMessage
            {
                Category = ExternalMessageCategory.Alarm,
                Title = $"{head} [{evt.VariableName}]",
                MarkdownText = $"## {head}\n"
                    + $"- 设备：{evt.DeviceKey}（{evt.DeviceId}）\n"
                    + $"- 变量：{evt.VariableName}（{evt.VariableKey}）\n"
                    + $"- 规则：{evt.RuleName ?? "-"}\n"
                    + $"- 级别：**{evt.Level}**\n"
                    + $"- 条件：{evt.Condition}  阈值：{evt.Threshold}\n"
                    + $"- 实际值：{evt.ActualValue}\n"
                    + $"- 来源：{evt.Source}\n"
                    + $"- 时间：{localTime:yyyy-MM-dd HH:mm:ss}",
                HtmlBody = $"<h3>{head}</h3>"
                    + "<table cellpadding='4' border='1'>"
                    + "<tr><th>设备</th><th>变量</th><th>规则</th><th>级别</th><th>条件/阈值</th><th>实际值</th><th>时间</th></tr>"
                    + $"<tr><td>{WebUtility.HtmlEncode(evt.DeviceKey)}(#{evt.DeviceId})</td>"
                    + $"<td>{WebUtility.HtmlEncode(evt.VariableName)}</td>"
                    + $"<td>{WebUtility.HtmlEncode(evt.RuleName ?? "-")}</td>"
                    + $"<td>{WebUtility.HtmlEncode(evt.Level.ToString())}</td>"
                    + $"<td>{WebUtility.HtmlEncode(evt.Condition?.ToString() ?? "-")} / {evt.Threshold}</td>"
                    + $"<td>{WebUtility.HtmlEncode(evt.ActualValue ?? "-")}</td>"
                    + $"<td>{localTime:yyyy-MM-dd HH:mm:ss}</td></tr></table>"
                    + $"<p>{WebUtility.HtmlEncode(evt.Message)}</p>"
            });
        }

        /// <inheritdoc/>
        public async Task NotifyScriptExecutionAsync(ScriptExecutionEvent evt)
        {
            await _inner.NotifyScriptExecutionAsync(evt);

            // 仅外发非 Success 结果（Success 每次都发会刷屏）。
            if (!_queue.HasEnabledChannels || !_policy.PushScript || evt.Result == "Success") return;

            var localTime = ToLocalDisplay(evt.StartedAt); // UTC -> 本地时区展示
            _queue.Enqueue(new ExternalMessage
            {
                Category = ExternalMessageCategory.ScriptExecution,
                Title = $"脚本执行异常 [#{evt.ScriptId} {evt.Result}]",
                MarkdownText = $"## 脚本执行异常\n"
                    + $"- 脚本ID：{evt.ScriptId}（v{evt.ScriptVersion}）\n"
                    + $"- 触发：{evt.TriggerSource}\n"
                    + $"- 结果：**{evt.Result}**\n"
                    + $"- 错误：{evt.Error ?? "-"}\n"
                    + $"- 耗时：{evt.DurationMs}ms\n"
                    + $"- 时间：{localTime:yyyy-MM-dd HH:mm:ss}",
                HtmlBody = "<h3>脚本执行异常</h3>"
                    + $"<p>脚本ID：{evt.ScriptId}（v{evt.ScriptVersion}）　触发：{WebUtility.HtmlEncode(evt.TriggerSource)}"
                    + $"　结果：<b>{WebUtility.HtmlEncode(evt.Result)}</b>　耗时：{evt.DurationMs}ms　时间：{localTime:yyyy-MM-dd HH:mm:ss}</p>"
                    + $"<pre>{WebUtility.HtmlEncode(evt.Error ?? "-")}</pre>"
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
