using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 外部消息推送服务（钉钉 / 邮件，单例 + IHostedService）。
    /// <para>
    /// 拓扑：主队列（有界 DropWrite）-> 扇出循环 -> 每渠道独立有界通道 + 独立消费循环。
    /// 各渠道完全隔离：单一渠道故障/限流/重试不阻塞其他渠道，入队非阻塞不拖累采集路径。
    /// 每渠道循环：固定窗口限流 -> 超限消息合并计数（下一条捎带说明）-> 失败重试（指数退避）。
    /// </para>
    /// <para>
    /// 停机顺序：完成主队列（扇出排空 <![CDATA[<=]]>3s）-> 完成各渠道通道（发送循环排空 <![CDATA[<=]]>22s）-> 超时才取消。
    /// 单条消息最坏 8s 超时 * 2 次尝试 + 1s 退避 = 17s < 22s，契合宿主 ShutdownTimeout=30s 预算。
    /// </para>
    /// </summary>
    public class ExternalNotificationService : IExternalNotificationQueue, IHostedService
    {
        /// <summary>
        /// 本服务日志类别：SystemLogRecorder 严重日志外发挂钩以此前缀排除本服务自身日志，防止递归。
        /// </summary>
        public const string LoggerCategory = "ScadaServer.WebApi.HostedServices.ExternalNotificationService";

        /// <summary>扇出排空超时（主队列完成 -> 扇出循环退出）。</summary>
        private static readonly TimeSpan FanoutDrainTimeout = TimeSpan.FromSeconds(3);

        /// <summary>渠道发送循环排空超时（各渠道完成 -> 消费循环退出）。</summary>
        private static readonly TimeSpan SendersDrainTimeout = TimeSpan.FromSeconds(22);

        private readonly List<SenderState> _states;
        private readonly IOptionsMonitor<NotificationOptions> _monitor;
        private readonly ILogger<ExternalNotificationService> _logger;
        private readonly INotificationLogRecorder _logRecorder;
        private readonly Channel<ExternalMessage> _mainChannel;
        private readonly CancellationTokenSource _cts = new();

        private Task? _fanoutTask;
        private long _enqueueDroppedCount;
        private long _fanoutDroppedCount;

        public ExternalNotificationService(
            IEnumerable<IExternalMessageSender> senders,
            IOptionsMonitor<NotificationOptions> options,
            INotificationLogRecorder logRecorder,
            ILogger<ExternalNotificationService> logger)
        {
            _monitor = options;
            _logger = logger;
            _logRecorder = logRecorder;

            // 构造期参数（Channel 容量 / 限流桶窗口）在启动时取一次快照；运行期参数走 Push 动态读取。
            var push = options.CurrentValue.Push;

            _mainChannel = Channel.CreateBounded<ExternalMessage>(new BoundedChannelOptions(Math.Max(64, push.QueueCapacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });

            // 全量保留全部渠道：是否启用的判定下放到扇出时动态判断，运行期启停渠道免重建通道。
            _states = senders
                .Select(s => new SenderState
                {
                    Sender = s,
                    Channel = Channel.CreateBounded<ExternalMessage>(new BoundedChannelOptions(Math.Max(64, push.QueueCapacity))
                    {
                        FullMode = BoundedChannelFullMode.DropWrite,
                        SingleReader = true
                    }),
                    Bucket = new RateBucket(TimeSpan.FromMinutes(1), push.MaxPerMinutePerChannel)
                })
                .ToList();
        }

        /// <summary>运行期推送参数（重试次数/退避）每次发送动态读取，保存后即时生效。</summary>
        private ExternalPushPolicy Push => _monitor.CurrentValue.Push;

        /// <inheritdoc/>
        public bool HasEnabledChannels => _states.Any(s => s.Sender.Enabled);

        /// <inheritdoc/>
        public bool Enqueue(ExternalMessage message)
        {
            // 无启用渠道直接短路：避免装饰器/日志挂钩白白格式化后积压至队列满。
            // 返回 false 供重试路径回写失败行（普通事件消息调用方忽略返回值）。
            if (!HasEnabledChannels) return false;

            if (!_mainChannel.Writer.TryWrite(message))
            {
                Interlocked.Increment(ref _enqueueDroppedCount);
                return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public bool IsChannelEnabled(string senderName) =>
            _states.Any(s => s.Sender.Enabled &&
                string.Equals(s.Sender.Name, senderName, StringComparison.OrdinalIgnoreCase));

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var enabled = _states.Where(s => s.Sender.Enabled).ToList();
            if (enabled.Count == 0)
            {
                _logger.LogInformation("钉钉/邮件/企业微信/Web Push 通知渠道均未启用，外部消息推送服务空闲。");
            }
            else
            {
                _logger.LogInformation("外部消息推送服务启动：{Count} 个渠道（{Channels}）。",
                    enabled.Count, string.Join("、", enabled.Select(s => s.Sender.Name)));
            }

            // 即使无启用渠道，扇出与消费循环仍启动（空转），以便运行期动态启用渠道免重建。
            _fanoutTask = FanoutAsync(_cts.Token);
            foreach (var state in _states)
            {
                state.Loop = SenderLoopAsync(state, _cts.Token);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // 1) 完成主队列写入端 -> 扇出循环排空剩余消息后自然退出。
            _mainChannel.Writer.TryComplete();

            if (_fanoutTask is not null)
            {
                try
                {
                    await _fanoutTask.WaitAsync(FanoutDrainTimeout);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("外部通知扇出循环排空超时。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "外部通知扇出循环退出异常。");
                }
            }

            // 2) 完成各渠道通道 -> 发送循环排空剩余消息（含最后一次重试）后自然退出。
            foreach (var state in _states)
            {
                state.Channel.Writer.TryComplete();
            }

            var loops = _states.Select(s => s.Loop).Where(t => t is not null).Select(t => t!).ToList();
            if (loops.Count > 0)
            {
                try
                {
                    await Task.WhenAll(loops).WaitAsync(SendersDrainTimeout);
                }
                catch (TimeoutException)
                {
                    // 3) 超时兜底才取消：中断卡住的网络调用，保证宿主 30s 关停预算不被拖穿。
                    _cts.Cancel();
                    _logger.LogWarning("外部通知渠道排空超时，已强制取消（剩余消息丢弃）。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "外部通知发送循环退出异常。");
                }
            }

            var enqueueDropped = Interlocked.Read(ref _enqueueDroppedCount);
            if (enqueueDropped > 0)
            {
                _logger.LogWarning("外部通知主队列满载丢弃 {Count} 条。", enqueueDropped);
            }
            var fanoutDropped = Interlocked.Read(ref _fanoutDroppedCount);
            if (fanoutDropped > 0)
            {
                _logger.LogWarning("外部通知渠道队列满载丢弃 {Count} 条。", fanoutDropped);
            }
        }

        /// <summary>扇出：主队列 -> 各启用渠道独立通道（某渠道满则该渠道丢弃并计数，不影响其他渠道）。
        /// TargetChannel 非空（重试路径）时只投递到指定渠道；定向消息被丢弃时回写失败行，避免行永久停留 Retrying。</summary>
        private async Task FanoutAsync(CancellationToken token)
        {
            try
            {
                await foreach (var msg in _mainChannel.Reader.ReadAllAsync(token))
                {
                    foreach (var state in _states)
                    {
                        // 动态跳过未启用渠道（渠道启停的竞态窗口由 IsChannelEnabled 前置校验兜底）。
                        if (!state.Sender.Enabled) continue;

                        // 重试路径：只投递到目标渠道
                        if (msg.TargetChannel is not null &&
                            !string.Equals(state.Sender.Name, msg.TargetChannel, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!state.Channel.Writer.TryWrite(msg))
                        {
                            Interlocked.Increment(ref _fanoutDroppedCount);
                            if (msg.SourceLogId is not null)
                            {
                                RecordOutcome(state.Sender, msg, "Failed", 0, "渠道队列满载，重试消息被丢弃");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 停机兜底取消：正常退出
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "外部通知扇出循环异常退出。");
            }
        }

        /// <summary>单渠道消费循环：限流 -> 合并超限计数 -> 重试发送。Suppressed 仅本循环线程读写。</summary>
        private async Task SenderLoopAsync(SenderState state, CancellationToken token)
        {
            try
            {
                await foreach (var msg in state.Channel.Reader.ReadAllAsync(token))
                {
                    if (!state.Bucket.TryConsume())
                    {
                        // 限流不静默丢：计数挂起，由下一条通过的消息捎带合并告知（报警风暴可追溯）。
                        state.Suppressed++;
                        continue;
                    }

                    var display = msg;
                    if (state.Suppressed > 0)
                    {
                        display = WithSuppressedNote(msg, state.Suppressed);
                        state.Suppressed = 0;
                    }

                    await SendWithRetryAsync(state.Sender, display, token);
                }
            }
            catch (OperationCanceledException)
            {
                // 停机兜底取消：正常退出
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "渠道 {Channel} 发送循环异常退出。", state.Sender.Name);
            }

            if (state.Suppressed > 0)
            {
                _logger.LogWarning("渠道 {Channel} 停止时仍有 {Count} 条被限流合并的消息未外发。", state.Sender.Name, state.Suppressed);
            }
        }

        /// <summary>重试发送（指数退避）。最终成功/失败均经投递记录埋点落库（终态）；失败仅记日志
        /// （本服务日志被系统日志挂钩排除，不会递归外发）。</summary>
        private async Task SendWithRetryAsync(IExternalMessageSender sender, ExternalMessage msg, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            var delay = Push.RetryBaseDelayMs;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await sender.SendAsync(msg, token);
                    sw.Stop();
                    RecordOutcome(sender, msg, "Success", sw.ElapsedMilliseconds, null);
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt >= Push.MaxAttempts)
                    {
                        _logger.LogError(ex, "渠道 {Channel} 发送失败（共尝试 {Attempts} 次），消息丢弃：{Title}",
                            sender.Name, attempt, msg.Title);
                        sw.Stop();
                        RecordOutcome(sender, msg, "Failed", sw.ElapsedMilliseconds, ex.Message);
                        return;
                    }

                    _logger.LogWarning(ex, "渠道 {Channel} 发送失败（第 {Attempt}/{Max} 次），{Delay}ms 后重试。",
                        sender.Name, attempt, Push.MaxAttempts, delay);
                    try
                    {
                        await Task.Delay(delay, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    delay *= 2;
                }
            }
        }

        /// <summary>投递终态埋点：每条消息每渠道完成（成功或重试耗尽）调用一次，写投递记录。
        /// Record 本身不抛出，包裹仅为兜底（埋点异常绝不影响发送主流程）。</summary>
        private void RecordOutcome(IExternalMessageSender sender, ExternalMessage msg, string status, long latencyMs, string? error)
        {
            try
            {
                _logRecorder.Record(new NotificationLogEntry(
                    Channel: MapChannel(sender.Name),
                    EventType: MapEventType(msg),
                    Title: msg.Title,
                    Recipient: sender.RecipientSummary,
                    Status: status,
                    LatencyMs: latencyMs,
                    Error: error,
                    PayloadPreview: Truncate(msg.MarkdownText, 500),
                    PayloadJson: JsonSerializer.Serialize(msg),
                    SourceLogId: msg.SourceLogId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "投递记录埋点异常（不影响发送主流程）。");
            }
        }

        /// <summary>Sender.Name → 前端渠道标识（已核实：DingTalk/Email/WebPush/WeCom）。</summary>
        private static string MapChannel(string senderName) => senderName.ToUpperInvariant() switch
        {
            "DINGTALK" => "dingTalk",
            "EMAIL" => "email",
            "WEBPUSH" => "webPush",
            "WECOM" => "weCom",
            _ => senderName.ToLowerInvariant()
        };

        /// <summary>消息类别 → 前端事件类型（Alarm 依 Tokens["eventType"] 细分触发/恢复，与前端联合类型对齐）。</summary>
        private static string MapEventType(ExternalMessage msg) => msg.Category switch
        {
            ExternalMessageCategory.Alarm =>
                msg.Tokens is not null && msg.Tokens.TryGetValue("eventType", out var t) && t == "Recovered"
                    ? "alarmRecovered"
                    : "alarmTriggered",
            ExternalMessageCategory.DeviceStatus => "deviceStatus",
            ExternalMessageCategory.SystemAlarm => "systemAlarm",
            ExternalMessageCategory.SystemError => "systemError",
            ExternalMessageCategory.ScriptExecution => "scriptExecution",
            _ => "unknown"
        };

        /// <summary>截断文本（PayloadPreview 用）。</summary>
        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];

        /// <summary>克隆消息并附加限流合并说明（HtmlBody 为空保持为空，保留兜底 markdown 转义路径）。
        /// Web Push 扩展字段（Severity/Tokens，D8）一并携带，保证渠道过滤/payload 构造在合并消息上仍可用。</summary>
        private static ExternalMessage WithSuppressedNote(ExternalMessage msg, int suppressed)
        {
            var note = $"另有 {suppressed} 条消息因限流未单独推送";
            return new ExternalMessage
            {
                Category = msg.Category,
                Title = msg.Title,
                MarkdownText = msg.MarkdownText + $"\n\n（{note}）",
                HtmlBody = msg.HtmlBody is null ? null : msg.HtmlBody + $"<p><b>（{note}）</b></p>",
                CreatedAtUtc = msg.CreatedAtUtc,
                Severity = msg.Severity,
                Tokens = msg.Tokens
            };
        }

        /// <summary>渠道独立状态（通道 / 限流桶 / 循环任务 / 超限计数）。</summary>
        private sealed class SenderState
        {
            public required IExternalMessageSender Sender { get; init; }
            public required Channel<ExternalMessage> Channel { get; init; }
            public required RateBucket Bucket { get; init; }
            public Task? Loop { get; set; }
            public int Suppressed { get; set; }
        }
    }

    /// <summary>固定窗口限流桶（每分钟 N 次）。</summary>
    internal sealed class RateBucket
    {
        private readonly object _gate = new();
        private readonly TimeSpan _window;
        private readonly int _limit;
        private DateTime _windowStart = DateTime.UtcNow;
        private int _count;

        public RateBucket(TimeSpan window, int limit)
        {
            _window = window;
            _limit = Math.Max(1, limit);
        }

        public bool TryConsume()
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if (now - _windowStart >= _window)
                {
                    _windowStart = now;
                    _count = 0;
                }
                if (_count >= _limit)
                {
                    return false;
                }
                _count++;
                return true;
            }
        }
    }
}
