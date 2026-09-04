using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
        private readonly ExternalPushPolicy _policy;
        private readonly ILogger<ExternalNotificationService> _logger;
        private readonly Channel<ExternalMessage> _mainChannel;
        private readonly CancellationTokenSource _cts = new();

        private Task? _fanoutTask;
        private long _enqueueDroppedCount;
        private long _fanoutDroppedCount;

        public ExternalNotificationService(
            IEnumerable<IExternalMessageSender> senders,
            IOptions<NotificationOptions> options,
            ILogger<ExternalNotificationService> logger)
        {
            _policy = options.Value.Push;
            _logger = logger;

            _mainChannel = Channel.CreateBounded<ExternalMessage>(new BoundedChannelOptions(Math.Max(64, _policy.QueueCapacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });

            // 只为启用渠道建立独立通道（未启用渠道不参与扇出与消费）。
            _states = senders
                .Where(s => s.Enabled)
                .Select(s => new SenderState
                {
                    Sender = s,
                    Channel = Channel.CreateBounded<ExternalMessage>(new BoundedChannelOptions(Math.Max(64, _policy.QueueCapacity))
                    {
                        FullMode = BoundedChannelFullMode.DropWrite,
                        SingleReader = true
                    }),
                    Bucket = new RateBucket(TimeSpan.FromMinutes(1), _policy.MaxPerMinutePerChannel)
                })
                .ToList();
        }

        /// <inheritdoc/>
        public bool HasEnabledChannels => _states.Count > 0;

        /// <inheritdoc/>
        public void Enqueue(ExternalMessage message)
        {
            // 无启用渠道直接短路：避免装饰器/日志挂钩白白格式化后积压至队列满。
            if (_states.Count == 0) return;

            if (!_mainChannel.Writer.TryWrite(message))
            {
                Interlocked.Increment(ref _enqueueDroppedCount);
            }
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_states.Count == 0)
            {
                _logger.LogInformation("钉钉/邮件通知渠道均未启用，外部消息推送服务空闲。");
                return Task.CompletedTask;
            }

            _logger.LogInformation("外部消息推送服务启动：{Count} 个渠道（{Channels}）。",
                _states.Count, string.Join("、", _states.Select(s => s.Sender.Name)));

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

        /// <summary>扇出：主队列 -> 各启用渠道独立通道（某渠道满则该渠道丢弃并计数，不影响其他渠道）。</summary>
        private async Task FanoutAsync(CancellationToken token)
        {
            try
            {
                await foreach (var msg in _mainChannel.Reader.ReadAllAsync(token))
                {
                    foreach (var state in _states)
                    {
                        if (!state.Channel.Writer.TryWrite(msg))
                        {
                            Interlocked.Increment(ref _fanoutDroppedCount);
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

        /// <summary>重试发送（指数退避）。最终失败仅记日志（本服务日志被系统日志挂钩排除，不会递归外发）。</summary>
        private async Task SendWithRetryAsync(IExternalMessageSender sender, ExternalMessage msg, CancellationToken token)
        {
            var delay = _policy.RetryBaseDelayMs;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await sender.SendAsync(msg, token);
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt >= _policy.MaxAttempts)
                    {
                        _logger.LogError(ex, "渠道 {Channel} 发送失败（共尝试 {Attempts} 次），消息丢弃：{Title}",
                            sender.Name, attempt, msg.Title);
                        return;
                    }

                    _logger.LogWarning(ex, "渠道 {Channel} 发送失败（第 {Attempt}/{Max} 次），{Delay}ms 后重试。",
                        sender.Name, attempt, _policy.MaxAttempts, delay);
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

        /// <summary>克隆消息并附加限流合并说明（HtmlBody 为空保持为空，保留兜底 markdown 转义路径）。</summary>
        private static ExternalMessage WithSuppressedNote(ExternalMessage msg, int suppressed)
        {
            var note = $"另有 {suppressed} 条消息因限流未单独推送";
            return new ExternalMessage
            {
                Category = msg.Category,
                Title = msg.Title,
                MarkdownText = msg.MarkdownText + $"\n\n（{note}）",
                HtmlBody = msg.HtmlBody is null ? null : msg.HtmlBody + $"<p><b>（{note}）</b></p>",
                CreatedAtUtc = msg.CreatedAtUtc
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
