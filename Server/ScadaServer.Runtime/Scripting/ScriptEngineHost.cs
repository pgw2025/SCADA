using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using Cronos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Runtime.Events;

namespace ScadaServer.Runtime.Scripting
{
    /// <summary>
    /// 脚本引擎宿主实现。
    /// <para>
    /// 调度模型：单一后台 Tick 循环（约 250ms），统一驱动 周期(Periodic) 与 Cron(Schedule)；
    /// 变量变化(OnChange) 通过订阅 <see cref="IVariableChangeBus"/> 驱动。避免每脚本一个 Timer，重载简单。
    /// 熔断：连续失败达阈值（默认 3）置 <see cref="SystemScript.Tripped"/>，调度跳过；手动 Run 可绕过。
    /// </para>
    /// <para>
    /// 执行模型（线程隔离）：所有触发仅向有界派发队列入队（微秒级返回，绝不内联执行），
    /// 由固定数量的专用消费线程（LongRunning，非线程池）出队执行。
    /// 动机：脚本 write 桥为有界同步阻塞（见 <see cref="ScriptRuntimeAccess"/>），若在
    /// DeviceWorker 采集线程（OnChange 同步事件回调）或调度 Tick 线程上执行，一次慢写入
    /// 即可拖停该设备采集 / 全局脚本调度，违背"采集循环永不被外部 IO 阻塞"原则。
    /// </para>
    /// <para>
    /// 防御纵深：① 队满丢弃（计数 + 限流告警，杜绝内存 OOM）；② 写桥超时封顶单次阻塞；
    /// ③ 挂死看门狗（调度 Tick 顺带扫描在途租约，超龄强制熔断并释放租约）；
    /// ④ 周期性观测日志（队列深度/丢弃/写桥超时计数）。
    /// </para>
    /// </summary>
    public class ScriptEngineHost : IScriptEngineHost, IHostedService
    {
        /// <summary>熔断连续失败阈值。</summary>
        public const int CircuitBreakerThreshold = 3;

        private static readonly TimeZoneInfo ScheduleZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

        /// <summary>专用消费线程数默认值（配置 Scripting:Consumers）。</summary>
        public const int DefaultConsumers = 2;

        /// <summary>派发队列容量默认值（配置 Scripting:QueueCapacity）。队满即丢弃触发（计数 + 告警）。</summary>
        public const int DefaultQueueCapacity = 256;

        /// <summary>
        /// Manual/Test 同步等待上界（含排队与执行）。需大于挂死看门狗阈值吗？——不需要：
        /// 二者独立生效；此上界仅防止 API 调用方无限等待（脚本写阻塞使挂钟时间可超过脚本自身 TimeoutMs）。
        /// </summary>
        private static readonly TimeSpan ManualAwaitTimeout = TimeSpan.FromSeconds(120);

        /// <summary>
        /// 挂死看门狗阈值：在途执行超过该时长视为挂死（未知阻塞源兜底：DB 挂起、无界 IO 等），
        /// 强制熔断脚本并释放租约。注意：被占用的消费线程无法回收（.NET 无 Thread.Abort），
        /// 仅标记与放行；线程级封底由写桥超时与驱动超时承担。
        /// </summary>
        private static readonly TimeSpan HangWatchdogTimeout = TimeSpan.FromSeconds(120);

        /// <summary>观测统计日志周期。</summary>
        private static readonly TimeSpan StatsLogInterval = TimeSpan.FromSeconds(60);

        /// <summary>队满丢弃告警的最小间隔（防高频触发刷屏）。</summary>
        private static readonly TimeSpan DropWarnInterval = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RuntimeManager _runtime;
        private readonly IVariableChangeBus _changeBus;
        private readonly IScadaNotificationService _notificationService;
        private readonly ILogger<ScriptEngineHost> _logger;
        private readonly ScriptRuntimeAccess _access;

        /// <summary>专用消费线程数。</summary>
        private readonly int _consumerCount;

        /// <summary>派发队列容量。</summary>
        private readonly int _queueCapacity;

        /// <summary>已加载的脚本作业快照（key = ScriptId）。</summary>
        private readonly ConcurrentDictionary<int, ScriptJob> _jobs = new();

        /// <summary>
        /// 正在执行中的脚本租约（key = ScriptId，防同一脚本并发重叠执行；值含开始时间供看门狗扫描）。
        /// 释放时按租约实例精确移除（TryRemove(KeyValuePair)），杜绝与看门狗/新一轮执行之间的误删竞态。
        /// </summary>
        private readonly ConcurrentDictionary<int, InflightLease> _inflight = new();

        /// <summary>周期性/Cron 调度轮询用 CancellationTokenSource。</summary>
        private CancellationTokenSource? _loopCts;

        /// <summary>调度循环 Task。</summary>
        private Task? _loopTask;

        /// <summary>有界派发队列；StartAsync 创建、StopAsync 完成。FullMode=Wait 使 TryWrite 满时返回 false（由入队方显式丢弃并计数）。</summary>
        private Channel<ScriptDispatchRequest>? _dispatchQueue;

        /// <summary>专用消费线程任务组（LongRunning：每任务独占一线程，不进线程池）。</summary>
        private Task[]? _consumers;

        /// <summary>停止中标志：置位后新触发不入队，队列残留项按 Skipped 落记录。</summary>
        private volatile bool _stopping;

        /// <summary>队满丢弃累计计数。</summary>
        private long _dispatchDropped;

        /// <summary>上次队满告警时间（Ticks）。</summary>
        private long _lastDropWarnTicks;

        /// <summary>上次观测统计日志时间。</summary>
        private DateTime _lastStatsLogUtc = DateTime.MinValue;

        public ScriptEngineHost(
            IServiceScopeFactory scopeFactory,
            RuntimeManager runtime,
            IVariableChangeBus changeBus,
            IScadaNotificationService notificationService,
            ILogger<ScriptEngineHost> logger,
            IConfiguration? configuration = null)
        {
            _scopeFactory = scopeFactory;
            _runtime = runtime;
            _changeBus = changeBus;
            _notificationService = notificationService;
            _logger = logger;

            _consumerCount = ReadConfigInt(configuration, "Scripting:Consumers", DefaultConsumers, 1, 16);
            _queueCapacity = ReadConfigInt(configuration, "Scripting:QueueCapacity", DefaultQueueCapacity, 8, 10000);
            var bridgeTimeoutMs = ReadConfigInt(configuration, "Scripting:WriteBridgeTimeoutMs",
                ScriptRuntimeAccess.DefaultWriteBridgeTimeoutMs, 500, 60000);

            _access = new ScriptRuntimeAccess(runtime, bridgeTimeoutMs, logger);
        }

        /// <summary>配置整数读取：null/非法取缺省，越界收敛到 [min, max]。</summary>
        private static int ReadConfigInt(IConfiguration? configuration, string key, int fallback, int min, int max)
        {
            return configuration != null && int.TryParse(configuration[key], out var value)
                ? Math.Clamp(value, min, max)
                : fallback;
        }

        // =============== IHostedService ===============

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stopping = false;
            _dispatchQueue = Channel.CreateBounded<ScriptDispatchRequest>(new BoundedChannelOptions(_queueCapacity)
            {
                SingleWriter = false,
                SingleReader = false,
                // FullMode=Wait：TryWrite 在队满时返回 false（不阻塞调用方、不静默丢弃），
                // 由入队方显式计数丢弃 + 限流告警。事件回调与调度 Tick 因此始终保持微秒级返回。
                FullMode = BoundedChannelFullMode.Wait
            });

            // 订阅变量变化（OnChange 触发）。订阅一次即可，重载只更新作业快照。
            // 回调内仅做匹配检查 + 入队，绝不在采集线程上内联执行脚本。
            _changeBus.VariableChanged += OnVariableChanged;

            await ReloadAsync();
            // ScheduleLoopAsync 本身返回热 Task，无需 Task.Run；保存引用供 StopAsync 等待退出。
            _loopTask = ScheduleLoopAsync(_loopCts.Token);
            StartConsumers();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _changeBus.VariableChanged -= OnVariableChanged;

            // 先置停止标志、完成队列：消费者排空缓冲项（按 [STOP] Skipped 落记录）后自行退出。
            _stopping = true;
            _dispatchQueue?.Writer.TryComplete();

            _loopCts?.Cancel();
            if (_loopTask is not null)
            {
                try
                {
                    // 等待调度循环退出；超时兜底防止宿主关闭被拖死。
                    await _loopTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("脚本调度循环停止超时。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "脚本调度循环退出异常。");
                }
            }

            // 等待消费者退出（含残留项按 Skipped 落记录）；超时放弃并告警，
            // 避免宿主关闭被挂死脚本（占用专用线程且无法回收）拖死。
            if (_consumers is { Length: > 0 })
            {
                try
                {
                    await Task.WhenAll(_consumers).WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("脚本消费者停止超时（可能有脚本挂死占用消费线程），放弃等待。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "脚本消费者退出异常。");
                }
            }

            if (!_inflight.IsEmpty)
            {
                _logger.LogWarning("停止时仍有 {Count} 个脚本在途执行（消费线程可能被挂死占用），放弃等待。", _inflight.Count);
            }

            // 全部收尾后才 Dispose，避免循环仍在使用 token 时触发 ObjectDisposedException。
            _loopCts?.Dispose();
            _loopCts = null;
            _jobs.Clear();
            _inflight.Clear();
        }

        // =============== 加载与重载 ===============

        /// <inheritdoc/>
        public async Task ReloadAsync()
        {
            try
            {
                List<SystemScript> scripts;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<ISystemScriptRepository>();
                    scripts = await repo.GetListAsync();
                }

                var next = new ConcurrentDictionary<int, ScriptJob>();
                var now = DateTime.UtcNow;

                // 仅启用 + 未熔断的脚本进入调度
                foreach (var s in scripts.Where(s => s.Active && !s.Tripped))
                {
                    next[s.Id] = new ScriptJob
                    {
                        Script = s,
                        NextUtc = ComputeNextRuntime(s, now)
                    };
                }

                _jobs.Clear();
                foreach (var (id, job) in next)
                {
                    _jobs[id] = job;
                }

                _logger.LogInformation("脚本引擎重载完成：共 {Total} 项启用脚本，{Scheduled} 项进入调度。",
                    scripts.Count, _jobs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本引擎重载失败，调度保持上一次状态。");
            }
        }

        /// <summary>
        /// 计算脚本下一次应触发的时间（UTC）。Manual 不参与调度；Periodic 用当前+间隔；Schedule 用 Cron 下一匹配。
        /// </summary>
        private static DateTime? ComputeNextRuntime(SystemScript s, DateTime nowUtc)
        {
            if (s.TriggerType == ScriptTriggerType.Manual.ToString())
            {
                return null;
            }
            if (s.TriggerType == ScriptTriggerType.Periodic.ToString())
            {
                var seconds = s.IntervalSeconds ?? 1;
                return nowUtc.AddSeconds(seconds);
            }
            if (s.TriggerType == ScriptTriggerType.Schedule.ToString())
            {
                try
                {
                    var cron = CronExpression.Parse(s.CronExpression ?? string.Empty);
                    // 传入 UTC 的 DateTime，Cronos 返回同 UtcKind 的下一次触发时间。
                    return cron.GetNextOccurrence(nowUtc, ScheduleZone);
                }
                catch
                {
                    return null;
                }
            }
            // OnChange 由事件驱动，不做时间调度。
            return null;
        }

        /// <summary>记录脚本状态快照的对象。</summary>
        private sealed class ScriptJob
        {
            public SystemScript Script { get; init; } = null!;
            public DateTime? NextUtc { get; set; }
            public DateTime? LastChangeAtUtc { get; set; }
        }

        /// <summary>在途执行租约：开始时间 + 触发类型，供挂死看门狗扫描。</summary>
        private sealed class InflightLease
        {
            public DateTime StartedAtUtc { get; init; }
            public string TriggerType { get; init; } = string.Empty;
        }

        /// <summary>
        /// 派发请求：自动触发不携带 Completion（fire-and-forget）；Manual/Test 携带
        /// TaskCompletionSource 以便 API 调用方异步等待结果。
        /// </summary>
        private sealed class ScriptDispatchRequest
        {
            public SystemScript Script { get; init; } = null!;
            public string TriggerType { get; init; } = string.Empty;
            public string? DeviceContextKey { get; init; }
            public string? VariableContextKey { get; init; }
            public string? ExecutedBy { get; init; }
            public ScriptSandbox.TriggerPayload? Payload { get; init; }
            public TaskCompletionSource<ScriptEngineResult>? Completion { get; init; }
        }

        // =============== 调度循环 ===============

        private async Task ScheduleLoopAsync(CancellationToken token)
        {
            using var tick = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            try
            {
                while (!token.IsCancellationRequested && await tick.WaitForNextTickAsync(token))
                {
                    var now = DateTime.UtcNow;
                    foreach (var job in _jobs.Values)
                    {
                        if (job.Script.TriggerType == ScriptTriggerType.Manual.ToString())
                        {
                            continue;
                        }

                        if (string.Equals(job.Script.TriggerType, ScriptTriggerType.OnChange.ToString()))
                        {
                            continue; // OnChange 由事件驱动
                        }

                        if (!job.Script.Active || job.Script.Tripped)
                        {
                            continue;
                        }

                        if (job.NextUtc == null)
                        {
                            continue;
                        }

                        if (now >= job.NextUtc.Value)
                        {
                            var dueType = job.Script.TriggerType;
                            job.NextUtc = ComputeNextRuntime(job.Script, now);
                            // 仅入队（微秒级），脚本执行永远不占用调度循环线程。
                            TryEnqueue(new ScriptDispatchRequest
                            {
                                Script = job.Script,
                                TriggerType = dueType
                            });
                        }
                    }

                    // 挂死看门狗与观测统计复用同一 Tick，不新增定时器。
                    CheckHungScripts(now);
                    LogDispatchStats(now);
                }
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出路径
            }
            catch (Exception ex)
            {
                // 未预期异常不能让调度循环静默死亡（fire-and-forget 时代无法察觉）。
                _logger.LogError(ex, "脚本调度循环因未预期异常退出。");
            }
        }

        // =============== 派发入队 ===============

        /// <summary>
        /// 入队派发请求。队满/引擎未运行时返回 false 并完成 <see cref="ScriptDispatchRequest.Completion"/>
        /// （调用方可立即取得 Skipped 结果）。丢弃计数 + 限流告警。
        /// </summary>
        private bool TryEnqueue(ScriptDispatchRequest request)
        {
            var queue = _dispatchQueue;
            if (queue == null || _stopping)
            {
                request.Completion?.TrySetResult(SkippedResult(request, "脚本引擎未运行"));
                return false;
            }

            if (queue.Writer.TryWrite(request))
            {
                return true;
            }

            Interlocked.Increment(ref _dispatchDropped);
            LogQueueFullThrottled(request);
            request.Completion?.TrySetResult(SkippedResult(request, "执行队列已满，本次触发被丢弃"));
            return false;
        }

        private static ScriptEngineResult SkippedResult(ScriptDispatchRequest request, string reason) => new()
        {
            ScriptId = request.Script.Id,
            ScriptVersion = request.Script.Version,
            Result = "Skipped",
            Error = reason + "。"
        };

        /// <summary>队满告警限流：最小间隔 30s，超间隔才记 Warning（含累计丢弃数）。</summary>
        private void LogQueueFullThrottled(ScriptDispatchRequest request)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastDropWarnTicks);
            if (nowTicks - last < DropWarnInterval.Ticks)
            {
                return;
            }
            Interlocked.CompareExchange(ref _lastDropWarnTicks, nowTicks, last);

            _logger.LogWarning(
                "脚本派发队列已满（容量 {Capacity}），触发被丢弃：ScriptId={ScriptId} Trigger={Trigger}；累计丢弃 {Dropped} 次（消费能力不足或脚本挂死，请检查消费线程数与看门狗日志）。",
                _queueCapacity, request.Script.Id, request.TriggerType, Interlocked.Read(ref _dispatchDropped));
        }

        // =============== 专用消费者 ===============

        /// <summary>
        /// 启动专用消费线程（LongRunning：每任务独占一线程，不进线程池）。
        /// 脚本执行（含写桥有界阻塞）全部发生在这些线程上，与采集/调度/HTTP 线程物理隔离。
        /// </summary>
        private void StartConsumers()
        {
            _consumers = Enumerable.Range(0, _consumerCount)
                .Select(_ => Task.Factory.StartNew(
                    static state => ((ScriptEngineHost)state!).ConsumeLoop(),
                    this,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();
        }

        /// <summary>
        /// 消费循环（同步阻塞模型，运行于专用线程）：阻塞等待队列 → 逐条同步执行。
        /// <para>
        /// 同步执行是有意设计：Jint 沙箱本身是同步的，写桥也是有界同步阻塞；在专用线程上
        /// 同步等待 <see cref="DispatchAsync"/> 内部的 await（DB 落库/SignalR）不会造成
        /// 线程池饥饿或死锁（专用线程非池线程、无 SynchronizationContext）。
        /// </para>
        /// </summary>
        private void ConsumeLoop()
        {
            var reader = _dispatchQueue!.Reader;
            try
            {
                while (true)
                {
                    if (!reader.TryRead(out var request))
                    {
                        // 无待处理项：阻塞等待（专用线程上阻塞是有意且安全的）；
                        // 通道已完成且缓冲排空时 WaitToReadAsync 返回 false → 退出。
                        if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                        {
                            break;
                        }
                        continue;
                    }

                    if (_stopping)
                    {
                        CompleteSkippedRequest(request, "服务停止");
                        continue;
                    }

                    ExecuteRequest(request);
                }
            }
            catch (Exception ex)
            {
                // 消费循环死亡 = 全部脚本停摆。ExecuteRequest 已兜底业务/落库异常，
                // 到此处的均为本循环自身缺陷，必须以 Error 暴露。
                _logger.LogError(ex, "脚本派发消费循环因未预期异常退出。");
            }
        }

        /// <summary>停机排空：残留请求按 Skipped 落记录并完成等待方。</summary>
        private void CompleteSkippedRequest(ScriptDispatchRequest request, string reason)
        {
            _ = PersistRecordAsync(request.Script, request.TriggerType, "Skipped", DateTime.UtcNow, 0, null,
                $"[STOP] {reason}，本次触发未执行", request.ExecutedBy);
            request.Completion?.TrySetResult(SkippedResult(request, reason));
        }

        /// <summary>
        /// 在消费线程上同步执行一次派发（含异常兜底），并完成等待方（Manual/Test）。
        /// </summary>
        private void ExecuteRequest(ScriptDispatchRequest request)
        {
            ScriptEngineResult? result;
            try
            {
                // 专用线程上同步等待：Jint 执行与写桥阻塞全程留在本线程；
                // 内部 await 的 DB/SignalR 部分 continuation 走线程池，最终回到本线程，无死锁环。
                result = DispatchAsync(request.Script, request.TriggerType, request.DeviceContextKey,
                    request.VariableContextKey, request.ExecutedBy, request.Payload).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // DispatchAsync 外层 try 无 catch，此处兜底 PersistRecord/Transmit 等链路异常。
                _logger.LogError(ex, "脚本 {Id} 派发链路异常（含执行记录落库失败）。", request.Script.Id);
                result = new ScriptEngineResult
                {
                    ScriptId = request.Script.Id,
                    ScriptVersion = request.Script.Version,
                    Result = "Error",
                    Error = "派发链路异常：" + ex.Message
                };
            }

            request.Completion?.TrySetResult(result ?? new ScriptEngineResult
            {
                ScriptId = request.Script.Id,
                ScriptVersion = request.Script.Version,
                Result = "Skipped",
                Error = "未能执行。"
            });
        }

        /// <summary>
        /// 执行一次脚本（仅在消费线程上调用）。
        /// </summary>
        private async Task<ScriptEngineResult?> DispatchAsync(
            SystemScript script,
            string triggerType,
            string? deviceContextKey,
            string? variableContextKey,
            string? executedBy = null,
            ScriptSandbox.TriggerPayload? payload = null)
        {
            var started = DateTime.UtcNow;

            // 并发保护：同一脚本上一次执行尚未结束则跳过（Skipped 不计入熔断失败计数）。
            // 繁忙判定在出队时（消费端）进行：入队到出队之间上一次执行可能已结束，
            // 队列中的触发仍有机会执行；同一脚本真正互斥由本 TryAdd 保证。
            var lease = new InflightLease { StartedAtUtc = started, TriggerType = triggerType };
            if (!_inflight.TryAdd(script.Id, lease))
            {
                await PersistRecordAsync(script, triggerType, "Skipped", started, 0, null,
                    "[BUSY] 上一次执行尚未结束，本次触发被跳过", executedBy);
                return new ScriptEngineResult
                {
                    ScriptId = script.Id,
                    ScriptVersion = script.Version,
                    Result = "Skipped",
                    Error = "上一次执行尚未结束。"
                };
            }

            try
            {
                // 熔断检查：自动触发且已熔断则跳过（记 Skipped）；手动 Run 在入口已绕过。
                if (script.Tripped && triggerType != "Manual" && triggerType != "Test")
                {
                    await PersistRecordAsync(script, triggerType, "Skipped", started, 0, null, "[TRIPPED] 脚本已熔断，本次被跳过", executedBy);
                    return null;
                }

                var sw = Stopwatch.StartNew();
                var result = new ScriptEngineResult
                {
                    ScriptId = script.Id,
                    ScriptVersion = script.Version,
                    Result = "Success"
                };
                string output;

                try
                {
                    var sandbox = new ScriptSandbox(script.Code, ClampTimeout(script.TimeoutMs), _access,
                        triggerType == "Test", script.ScopeRead, script.ScopeWrite);
                    if (payload != null)
                    {
                        output = sandbox.OnChange(payload);
                    }
                    else
                    {
                        output = sandbox.Run();
                    }
                    result.Output = output;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    var isTimeout = ex is TimeoutException
                        || (ex.Message?.Contains("timeout", StringComparison.OrdinalIgnoreCase) ?? false);
                    result.Result = isTimeout ? "Timeout" : "Error";
                    result.Error = ex.Message;
                    result.Output = string.Empty;

                    await RecordFailureAsync(script, triggerType, started, sw.ElapsedMilliseconds, ex.Message, executedBy, result.Result);
                    await TransmitAsync(result, triggerType, executedBy, wroteLog: false);
                    return result;
                }

                sw.Stop();
                result.DurationMs = (int)sw.ElapsedMilliseconds;

                // 成功：清零失败计数并刷新最近执行元信息。
                if (script.FailureCount != 0 || script.LastExecutedAt == null)
                {
                    script.FailureCount = 0;
                    script.LastError = null;
                    script.LastExecutedAt = started;
                    script.LastDurationMs = result.DurationMs;
                    await PersistScriptStatusAsync(script);
                }

                // 落执行记录
                var persisted = await PersistRecordAsync(script, triggerType, "Success", started, result.DurationMs, null, output, executedBy);
                result.WroteLog = persisted;
                await TransmitAsync(result, triggerType, executedBy, result.WroteLog);

                return result;
            }
            finally
            {
                // 按租约实例精确移除：若看门狗已先行释放并允许新一轮执行占位，
                // 此处不得误删新一轮的租约（TryRemove(KVP) 仅在当前值等于本租约时移除）。
                _inflight.TryRemove(KeyValuePair.Create(script.Id, lease));
            }
        }

        /// <summary>
        /// 异常/超时后更新失败的脚本（熔断计数、熔断标记、最近错误），并记录执行记录。
        /// </summary>
        private async Task RecordFailureAsync(SystemScript script, string triggerType, DateTime started, long durationMs, string error, string? executedBy, string resultType = "Error")
        {
            string msg = error == null ? string.Empty : error.Length > 2000 ? error[..2000] : error;

            script.FailureCount += 1;
            script.LastError = msg;
            script.LastExecutedAt = started;
            script.LastDurationMs = (int)durationMs;
            if (script.FailureCount >= CircuitBreakerThreshold)
            {
                script.Tripped = true;
            }
            await PersistScriptStatusAsync(script);

            await PersistRecordAsync(script, triggerType, resultType, started, (int)durationMs, msg, null, executedBy);
        }

        // =============== 挂死看门狗 ===============

        /// <summary>
        /// 扫描在途租约，超龄（&gt; <see cref="HangWatchdogTimeout"/>）视为挂死：
        /// Error 日志 + 强制熔断（Tripped）+ 释放租约（允许后续触发，防 [BUSY] 永久静默）
        /// + 落 "Timeout" 执行记录。被占用的消费线程无法回收（.NET 无 Thread.Abort），
        /// 这是未知阻塞源的兜底防线；线程级封底由写桥超时/驱动超时承担。
        /// </summary>
        private void CheckHungScripts(DateTime utcNow)
        {
            foreach (var (scriptId, lease) in _inflight)
            {
                var elapsed = utcNow - lease.StartedAtUtc;
                if (elapsed < HangWatchdogTimeout)
                {
                    continue;
                }

                // 原子移除（仅当租约仍是当前实例）：与消费者正常释放 / 挂死后新一轮执行竞态隔离。
                if (!_inflight.TryRemove(KeyValuePair.Create(scriptId, lease)))
                {
                    continue;
                }

                _logger.LogError(
                    "脚本 {ScriptId} 执行挂死（{Seconds:F0}s 未完成，Trigger={Trigger}），看门狗强制释放租约并熔断；对应消费线程可能仍被占用。",
                    scriptId, elapsed.TotalSeconds, lease.TriggerType);

                var script = _jobs.TryGetValue(scriptId, out var job) ? job.Script : null;
                if (script == null)
                {
                    continue;
                }

                script.Tripped = true;
                script.LastError = $"执行挂死（{elapsed.TotalSeconds:F0}s），已被看门狗强制熔断";
                script.LastExecutedAt = lease.StartedAtUtc;
                script.LastDurationMs = (int)elapsed.TotalMilliseconds;

                // 两个持久化方法内部均有异常兜底（仅记日志），fire-and-forget 安全。
                _ = PersistScriptStatusAsync(script);
                _ = PersistRecordAsync(script, lease.TriggerType, "Timeout", lease.StartedAtUtc,
                    (int)elapsed.TotalMilliseconds, script.LastError, null, null);
            }
        }

        // =============== 观测统计 ===============

        /// <summary>
        /// 周期性观测日志（默认 60s 一次）：仅在存在异常信号（队列积压/丢弃/写桥超时/在途）
        /// 时输出，正常运行零噪音。告警建议：队列深度持续超过容量 50% 且不回落 = 消费能力不足。
        /// </summary>
        private void LogDispatchStats(DateTime utcNow)
        {
            if (utcNow - _lastStatsLogUtc < StatsLogInterval)
            {
                return;
            }
            _lastStatsLogUtc = utcNow;

            var dropped = Interlocked.Read(ref _dispatchDropped);
            var bridgeTimeouts = _access.BridgeTimeoutCount;
            var depth = _dispatchQueue?.Reader.Count ?? 0;
            if (dropped == 0 && bridgeTimeouts == 0 && depth == 0 && _inflight.IsEmpty)
            {
                return;
            }

            _logger.LogInformation(
                "脚本派发观测：队列深度={Depth}/{Capacity}，累计丢弃={Dropped}，写桥超时={BridgeTimeouts}，在途执行={Inflight}，消费线程={Consumers}",
                depth, _queueCapacity, dropped, bridgeTimeouts, _inflight.Count, _consumerCount);
        }

        // =============== 公开执行接口 ===============

        /// <inheritdoc/>
        public async Task<ScriptEngineResult> RunAsync(int scriptId, string executedBy)
        {
            // 从作业快照取最新，若不存在则回源库读（手动 Run 允许熔断态脚本）。
            var script = _jobs.TryGetValue(scriptId, out var job) ? job.Script : await LoadScriptByIdAsync(scriptId);
            if (script == null)
            {
                return new ScriptEngineResult { ScriptId = scriptId, Result = "Error", Error = "脚本不存在。" };
            }

            return await EnqueueAndWaitAsync(script, "Manual", null, null, executedBy, null);
        }

        /// <inheritdoc/>
        public async Task<ScriptEngineResult> TestAsync(SystemScript script, string? deviceContextKey, string? variableContextKey, string executedBy)
        {
            var payload = (deviceContextKey != null && variableContextKey != null)
                ? new ScriptSandbox.TriggerPayload
                {
                    DeviceKey = deviceContextKey,
                    VariableKey = variableContextKey,
                    Value = _access.Read(deviceContextKey, variableContextKey),
                    PreviousValue = null,
                    Quality = _access.GetQuality(deviceContextKey, variableContextKey)
                }
                : null;

            return await EnqueueAndWaitAsync(script, "Test", deviceContextKey, variableContextKey, executedBy, payload);
        }

        /// <summary>
        /// 入队并异步等待执行结果（Manual/Test）。等待有上界（<see cref="ManualAwaitTimeout"/>，
        /// 含排队与执行）：超时返回 Timeout 结果，实际执行结果以后续执行记录/推送为准。
        /// </summary>
        private async Task<ScriptEngineResult> EnqueueAndWaitAsync(
            SystemScript script,
            string triggerType,
            string? deviceContextKey,
            string? variableContextKey,
            string? executedBy,
            ScriptSandbox.TriggerPayload? payload)
        {
            var request = new ScriptDispatchRequest
            {
                Script = script,
                TriggerType = triggerType,
                DeviceContextKey = deviceContextKey,
                VariableContextKey = variableContextKey,
                ExecutedBy = executedBy,
                Payload = payload,
                Completion = new TaskCompletionSource<ScriptEngineResult>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            if (!TryEnqueue(request))
            {
                // 入队失败（队列满/引擎未运行）：TryEnqueue 已完成 Completion，立即返回 Skipped。
                return await request.Completion.Task;
            }

            try
            {
                return await request.Completion.Task.WaitAsync(ManualAwaitTimeout);
            }
            catch (TimeoutException)
            {
                return new ScriptEngineResult
                {
                    ScriptId = script.Id,
                    ScriptVersion = script.Version,
                    Result = "Timeout",
                    Error = $"执行等待超时（>{ManualAwaitTimeout.TotalSeconds:F0}s，含排队与执行时间）；实际执行结果以脚本执行记录为准。"
                };
            }
        }

        // =============== OnChange ===============

        private void OnVariableChanged(object? sender, VariableChangeEvent e)
        {
            // 解析设备键
            string deviceKey;
            if (_runtime.DeviceRuntimes.TryGetValue(e.DeviceId, out var runtime))
            {
                deviceKey = runtime.Device.Key;
            }
            else
            {
                return;
            }

            foreach (var job in _jobs.Values)
            {
                var s = job.Script;
                if (s.TriggerType != ScriptTriggerType.OnChange.ToString() || !s.Active || s.Tripped)
                {
                    continue;
                }
                if (!string.Equals(s.WatchDeviceKey, deviceKey, StringComparison.Ordinal)
                    || !string.Equals(s.WatchVariableKey, e.VariableKey, StringComparison.Ordinal))
                {
                    continue;
                }

                // 死区：|new - old| > DeadBand 才触发
                if (s.DeadBand.HasValue && s.DeadBand.Value > 0
                    && e.PreviousValue != null && e.Value != null
                    && TryToDouble(e.PreviousValue, out var prev) && TryToDouble(e.Value, out var cur))
                {
                    if (Math.Abs(cur - prev) <= s.DeadBand.Value)
                    {
                        continue;
                    }
                }

                // 冷却：抑制高频抖动与回声
                var now = DateTime.UtcNow;
                if (job.LastChangeAtUtc.HasValue && (now - job.LastChangeAtUtc.Value).TotalMilliseconds < s.CooldownMs)
                {
                    continue;
                }
                job.LastChangeAtUtc = now;

                var payload = new ScriptSandbox.TriggerPayload
                {
                    DeviceKey = deviceKey,
                    VariableKey = e.VariableKey,
                    Value = e.Value,
                    PreviousValue = e.PreviousValue,
                    Quality = e.Quality.ToString()
                };

                // 仅入队（微秒级返回）：本回调运行在 DeviceWorker 采集线程上，
                // 脚本执行（含写桥阻塞）绝不允许发生在采集线程。
                TryEnqueue(new ScriptDispatchRequest
                {
                    Script = s,
                    TriggerType = "OnChange",
                    DeviceContextKey = deviceKey,
                    VariableContextKey = e.VariableKey,
                    Payload = payload
                });
            }
        }

        // =============== 持久化与推送 ===============

        private async Task<bool> PersistRecordAsync(
            SystemScript script, string triggerType, string result,
            DateTime started, int? durationMs, string? error, string? output, string? executedBy)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IScriptExecutionRecordRepository>();
                var record = new ScriptExecutionRecord
                {
                    ScriptId = script.Id,
                    ScriptVersion = script.Version,
                    TriggerSource = triggerType,
                    Result = result,
                    StartedAt = started,
                    DurationMs = durationMs,
                    Error = error == null ? null : (error.Length > 4000 ? error[..4000] : error),
                    Output = output == null ? null : (output.Length > 8000 ? output[..8000] : output),
                    ExecutedBy = executedBy
                };
                await repo.InsertAsync(record);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本执行记录落库失败。ScriptId={Id}", script.Id);
                return false;
            }
        }

        private async Task PersistScriptStatusAsync(SystemScript script)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ISystemScriptRepository>();
                await repo.UpdateAsync(script);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本状态持久化失败。ScriptId={Id}", script.Id);
            }
        }

        private async Task TransmitAsync(ScriptEngineResult result, string triggerType, string? executedBy, bool wroteLog)
        {
            try
            {
                await _notificationService.NotifyScriptExecutionAsync(new ScriptExecutionEvent
                {
                    ScriptId = result.ScriptId,
                    ScriptVersion = result.ScriptVersion,
                    TriggerSource = triggerType,
                    Result = result.Result,
                    StartedAt = DateTime.UtcNow,
                    DurationMs = result.DurationMs,
                    Error = result.Error,
                    Output = result.Output,
                    ExecutedBy = executedBy
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "脚本执行事件推送失败。ScriptId={Id}", result.ScriptId);
            }
        }

        /// <summary>
        /// 从作业快照加载最近脚本；快照缺失时回源库读取。
        /// </summary>
        private async Task<SystemScript?> LoadScriptByIdAsync(int scriptId)
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISystemScriptRepository>();
            return await repo.GetByIdAsync(scriptId);
        }

        private static int ClampTimeout(int timeoutMs) => Math.Clamp(timeoutMs, 500, 30000);

        private static bool TryToDouble(object value, out double result)
        {
            try
            {
                result = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }
    }
}
