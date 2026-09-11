using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Runtime;
using ScadaServer.Runtime.Events;
using ScadaServer.Runtime.Interface;

namespace ScadaServer.Runtime.Bindings;

/// <inheritdoc cref="IVariableBindingEngine"/>
/// <remarks>
/// 阶段二+三：单向/多跳 OnChange 转发，含加载期环检测、回声加固与并发修正支持。
/// 根因 A/B 强化：写入失败分类 + 有限重试 + 离线补写 + 越限策略 + 连续失败告警 + 设备就绪重载。
/// 环与自环在加载期拒绝；多跳链在防回环前提下支持；回声基值记录于 VariableRuntime.LastBindingWriteValue。
/// </remarks>
public sealed class VariableBindingEngine : IVariableBindingEngine
{
    private readonly IVariableChangeBus _changeBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VariableBindingEngine> _logger;
    private readonly IScadaNotificationService _notificationService;

    // 绑定索引：(源设备Id, 源变量Key) -> 目标列表。volatile 赋值保证读线程立即可见。
    private volatile Dictionary<(int DeviceId, string VariableKey), List<BindingTarget>> _index = new();

    // 加载状态快照（volatile 原子替换，供规则列表接口读取）：
    // _skipReasons 记录确定性跳过（变量不存在/目标只读），_pendingReasons 记录设备未就绪待重试。
    private volatile IReadOnlyDictionary<int, string> _skipReasons = new Dictionary<int, string>();
    private volatile IReadOnlyDictionary<int, string> _pendingReasons = new Dictionary<int, string>();

    // 运行指标（Interlocked 更新），后续可接入健康检查/监控端点。
    private long _writeSuccess;
    private long _writeFail;

    // 转发写入队列：事件回调只做非阻塞入队，由单消费者循环串行处理，
    // 消除原先每次转发一个 Task.Run 的无上限并发（fire-and-forget）。
    private readonly Channel<(BindingTarget Target, object? Value)> _writeChannel
        = Channel.CreateBounded<(BindingTarget, object?)>(new BoundedChannelOptions(10_000)
        {
            // 背压策略：队列满时丢最旧的待转发项，保证采集线程（事件发布方）永不阻塞。
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    private CancellationTokenSource? _cts;
    private Task? _dispatchLoop;
    private long _droppedCount;

    // 离线补写通道（根因 A3）：每目标 (设备,变量) 仅保留最新值，避免补写风暴。
    // 值/策略/时间戳在写入失败（重试耗尽）时写入，设备恢复 Online 或定时兜底时补写。
    private readonly ConcurrentDictionary<(int DeviceId, string VariableKey), PendingWrite> _pendingWrites = new();

    // 目标级连续失败计数（根因 A5）：仅瞬时可重试失败累计，达到阈值发系统报警后复位。
    private readonly ConcurrentDictionary<(int DeviceId, string VariableKey), int> _consecutiveFailures = new();

    // 去抖重载（根因 B1）：0/1 单飞标记 + 上次加载时间（1s 最小重载间隔）。
    private int _reloadPending;
    private DateTime _lastLoadUtc = DateTime.MinValue;

    // 已推送环路报警集合（进程内去重，根因 B1）：仅当环路集合变化时才推送，避免频繁重载重复告警。
    private readonly object _cycleAlarmGate = new();
    private HashSet<string> _notifiedCycleKeys = new();

    // 重试退避（根因 A2）：3 次重试，指数退避 500ms → 1s → 2s（合计 ≈3.5s）。
    private static readonly int[] RetryDelayMs = { 500, 1000, 2000 };
    private const int ConsecutiveFailureAlarmThreshold = 10;
    private const int PendingRetryIntervalMs = 1000;

    public VariableBindingEngine(
        IVariableChangeBus changeBus,
        IServiceProvider serviceProvider,
        ILogger<VariableBindingEngine> logger,
        IScadaNotificationService notificationService)
    {
        _changeBus = changeBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _notificationService = notificationService;
        _changeBus.VariableChanged += OnVariableChanged;
    }

    /// <inheritdoc/>
    public async Task LoadAsync()
    {
        // RM 为 Singleton，此处解析到的即为当前运行实例。
        var rm = _serviceProvider.GetRequiredService<RuntimeManager>();

        var skipReasons = new Dictionary<int, string>();
        var pendingReasons = new Dictionary<int, string>();

        // 1) 解析并校验所有 active 绑定 -> 候选边（设备/变量存在、目标非只读）。
        var candidates = new List<BindingCandidate>();
        using (var scope = _serviceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IDataConversionRepository>();
            var conversions = await repo.GetListAsync();

            foreach (var c in conversions.Where(x => x.Active))
            {
                if (!rm.DeviceRuntimes.TryGetValue(c.SourceDeviceId, out var srcRt))
                {
                    _logger.LogWarning("变量绑定跳过：源设备 {DeviceId} 未运行。绑定={Name}", c.SourceDeviceId, c.Name);
                    pendingReasons[c.Id] = "源设备未运行";
                    continue;
                }
                if (!rm.DeviceRuntimes.TryGetValue(c.TargetDeviceId, out var tgtRt))
                {
                    _logger.LogWarning("变量绑定跳过：目标设备 {DeviceId} 未运行。绑定={Name}", c.TargetDeviceId, c.Name);
                    pendingReasons[c.Id] = "目标设备未运行";
                    continue;
                }
                if (!srcRt.Variables.Values.Any(v => v.Key == c.SourceVariableKey))
                {
                    _logger.LogWarning("变量绑定跳过：源变量 {Key} 在设备 {DeviceId} 不存在。绑定={Name}", c.SourceVariableKey, c.SourceDeviceId, c.Name);
                    skipReasons[c.Id] = "源变量不存在";
                    continue;
                }
                var tgtVar = tgtRt.Variables.Values.FirstOrDefault(v => v.Key == c.TargetVariableKey);
                if (tgtVar == null)
                {
                    _logger.LogWarning("变量绑定跳过：目标变量 {Key} 在设备 {DeviceId} 不存在。绑定={Name}", c.TargetVariableKey, c.TargetDeviceId, c.Name);
                    skipReasons[c.Id] = "目标变量不存在";
                    continue;
                }
                if (tgtVar.IsReadOnly)
                {
                    _logger.LogWarning("变量绑定跳过：目标变量 {Key} 在设备 {DeviceId} 为只读。绑定={Name}", c.TargetVariableKey, c.TargetDeviceId, c.Name);
                    skipReasons[c.Id] = "目标变量只读";
                    continue;
                }

                candidates.Add(new BindingCandidate(
                    c.SourceDeviceId, c.SourceVariableKey,
                    c.TargetDeviceId, c.TargetVariableKey,
                    c.Name, c.OutOfRangePolicy));
            }
        }

        // 2) 环检测（自环 + 多节点环），拒绝相关绑定并收集名字→详情（供去重告警）。
        var rejectedDetails = DetectAndRejectCycles(candidates);

        // 3) 构建索引（仅合法绑定）。
        var newIndex = new Dictionary<(int, string), List<BindingTarget>>();
        foreach (var cand in candidates.Where(c => !c.Rejected))
        {
            var key = (cand.SourceDeviceId, cand.SourceVariableKey);
            if (!newIndex.TryGetValue(key, out var list))
            {
                list = new List<BindingTarget>();
                newIndex[key] = list;
            }
            list.Add(new BindingTarget(cand.TargetDeviceId, cand.TargetVariableKey, cand.OutOfRangePolicy));
        }

        _index = newIndex;
        _skipReasons = skipReasons;
        _pendingReasons = pendingReasons;
        _logger.LogInformation("变量绑定引擎已加载 {Count} 条源映射，拒绝 {Rejected} 条（环路/非法），跳过 {Skipped} 条，等待 {Pending} 条。",
            newIndex.Count, rejectedDetails.Count, skipReasons.Count, pendingReasons.Count);

        // 4) 环路告警去重：仅当环路集合较上次变化时推送新告警（避免频繁重载重复推送）。
        NotifyNewCycleAlarms(rejectedDetails);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // DispatchLoopAsync 本身返回热 Task，无需 Task.Run；保存引用供 StopAsync 等待退出。
        _dispatchLoop = DispatchLoopAsync(_cts.Token);

        // 订阅设备状态变更：设备恢复 Online 时，触发绑定重载（B1）并补写离线值（A3）。
        var rm = _serviceProvider.GetRequiredService<RuntimeManager>();
        rm.StatusChanged += OnDeviceStatusChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // 退订状态变更，避免停止后仍被触发。
        var rm = _serviceProvider.GetRequiredService<RuntimeManager>();
        rm.StatusChanged -= OnDeviceStatusChanged;

        // 先关闭通道让循环排空剩余转发，再取消，最后等待退出。
        _writeChannel.Writer.TryComplete();
        _cts?.Cancel();
        if (_dispatchLoop is not null)
        {
            try
            {
                // 等待循环排空完成；超时兜底防止宿主关闭被拖死。
                await _dispatchLoop.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("变量绑定转发循环停止超时，剩余转发可能未完成。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "变量绑定转发循环退出异常。");
            }
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _index = new Dictionary<(int, string), List<BindingTarget>>();
    }

    /// <inheritdoc/>
    public void ScheduleReload()
    {
        if (Interlocked.CompareExchange(ref _reloadPending, 1, 0) == 0)
        {
            _ = ReloadDebouncedAsync();
        }
    }

    /// <summary>
    /// 运行指标只读快照（成功/失败/补写队列长度/丢弃次数），供后续监控接入。
    /// </summary>
    public (long Success, long Fail, long Pending, long Dropped) GetStats() =>
        (Interlocked.Read(ref _writeSuccess), Interlocked.Read(ref _writeFail),
         _pendingWrites.Count, Interlocked.Read(ref _droppedCount));

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> GetSkipReasons() => _skipReasons;

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> GetPendingReasons() => _pendingReasons;

    private void OnVariableChanged(object? sender, VariableChangeEvent evt)
    {
        // 跳过引擎自身写入产生的事件，避免即时回环（多跳链的二次传播依赖环检测拦截）。
        if (evt.Source == VariableChangeSource.BindingWrite)
        {
            return;
        }

        var local = _index;
        if (!local.TryGetValue((evt.DeviceId, evt.VariableKey), out var targets) || targets.Count == 0)
        {
            return;
        }

        // 转发写入入队由单消费者循环处理，避免阻塞采集循环/写入通道（事件总线为同步回调）。
        foreach (var t in targets)
        {
            if (!_writeChannel.Writer.TryWrite((t, evt.Value)))
            {
                Interlocked.Increment(ref _droppedCount);
            }
        }
    }

    private void OnDeviceStatusChanged(object? sender, DeviceStatusChangedEventArgs e)
    {
        if (e.Status != DeviceStatus.Online)
        {
            return;
        }

        // 设备就绪：触发绑定重载（此前 pending 的规则补加载）+ 补写离线值。
        ScheduleReload();
        _ = RetryPendingWritesAsync(e.DeviceId);
    }

    /// <summary>
    /// 单消费者循环：串行排空待转发写入（天然限流 + 保序），同目标合并保留最新值，消除重试期间堆积。
    /// 每轮末做一次补写兜底扫描（根因 A3 定时兜底）。
    /// </summary>
    private async Task DispatchLoopAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                bool hasItem;
                try
                {
                    hasItem = await _writeChannel.Reader.WaitToReadAsync(token).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(1));
                }
                catch (TimeoutException)
                {
                    hasItem = false;
                }

                if (hasItem)
                {
                    // 排空当前队列：同目标只保留最新值（最新值语义，避免重试期间旧条目堆积）。
                    var batch = new Dictionary<(int, string), (object? Value, string Policy)>();
                    var order = new List<(int, string)>();
                    while (_writeChannel.Reader.TryRead(out var item))
                    {
                        var key = (item.Target.DeviceId, item.Target.VariableKey);
                        if (!batch.ContainsKey(key))
                        {
                            order.Add(key);
                        }
                        batch[key] = (item.Value, item.Target.OutOfRangePolicy);
                    }

                    foreach (var key in order)
                    {
                        token.ThrowIfCancellationRequested();
                        var (value, policy) = batch[key];
                        await WriteTargetWithRetryAsync(new BindingTarget(key.Item1, key.Item2, policy), value, token);
                    }
                }

                // 定时兜底补写：设备恢复但暂无新转发触发时补写此前离线值。
                await RetryPendingWritesAsync();

                // 通道已完成且已排空 → 正常退出。
                if (!hasItem && _writeChannel.Reader.Completion.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 应用关闭：正常退出路径
        }
        catch (Exception ex)
        {
            // WriteTargetWithRetryAsync 内部已捕获业务异常；此处兜底枚举器等链路异常。
            _logger.LogError(ex, "变量绑定转发循环因未预期异常退出。");
        }

        if (Interlocked.Read(ref _droppedCount) > 0)
        {
            _logger.LogWarning("变量绑定转发队列满载丢弃 {Count} 条。", Interlocked.Read(ref _droppedCount));
        }
    }

    /// <summary>
    /// 带有限重试的目标写入：瞬时可重试失败按 500ms→1s→2s 指数退避，共 3 次重试；
    /// 重试耗尽后落入离线补写通道，待设备恢复补写最新值。确定性失败（变量不存在/只读/越限 Reject）不重试。
    /// </summary>
    private async Task WriteTargetWithRetryAsync(BindingTarget target, object? value, CancellationToken token)
    {
        if (value == null)
        {
            return;
        }

        var maxRetries = RetryDelayMs.Length;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var result = await TryWriteOnceAsync(target, value);
            if (result.Success)
            {
                OnWriteSuccess(target, value);
                return;
            }

            OnWriteFailure(target, result);

            var retryable = IsRetryable(result.FailureKind);
            if (attempt >= maxRetries || !retryable)
            {
                // 瞬时可重试失败耗尽 → 落入补写通道（保留最新值，待设备恢复补写）。
                if (retryable)
                {
                    _pendingWrites[(target.DeviceId, target.VariableKey)] =
                        new PendingWrite(value, target.OutOfRangePolicy, DateTime.UtcNow);
                }
                return;
            }

            await Task.Delay(RetryDelayMs[attempt], token);
        }
    }

    /// <summary>单次写入尝试（无重试）。</summary>
    private async Task<(bool Success, string? ErrorMessage, VariableWriteFailureKind FailureKind)> TryWriteOnceAsync(BindingTarget target, object value)
    {
        var rm = _serviceProvider.GetRequiredService<RuntimeManager>();
        return await rm.WriteVariableAsync(target.DeviceId, target.VariableKey, value, "变量绑定", target.OutOfRangePolicy);
    }

    private void OnWriteSuccess(BindingTarget target, object value)
    {
        Interlocked.Increment(ref _writeSuccess);
        _consecutiveFailures.TryRemove((target.DeviceId, target.VariableKey), out _);

        // 回声加固：记录期望值与时间戳，供目标变量后续轮询回读时抑制回显事件。
        var rm = _serviceProvider.GetRequiredService<RuntimeManager>();
        if (rm.DeviceRuntimes.TryGetValue(target.DeviceId, out var tr))
        {
            var tv = tr.Variables.Values.FirstOrDefault(v => v.Key == target.VariableKey);
            if (tv != null)
            {
                tv.LastBindingWriteValue = value;
                tv.LastBindingWriteTime = DateTime.UtcNow;
            }
        }
    }

    private void OnWriteFailure(BindingTarget target, (bool Success, string? ErrorMessage, VariableWriteFailureKind FailureKind) result)
    {
        Interlocked.Increment(ref _writeFail);
        _logger.LogWarning("变量绑定写入失败：{DeviceId}/{Key}：{Msg}", target.DeviceId, target.VariableKey, result.ErrorMessage);

        if (!IsRetryable(result.FailureKind))
        {
            return; // 确定性配置问题：由保存期校验拦截（根因 C），此处仅记日志兜底。
        }

        var key = (target.DeviceId, target.VariableKey);
        var consecutive = _consecutiveFailures.AddOrUpdate(key, 1, static (_, v) => v + 1);
        if (consecutive >= ConsecutiveFailureAlarmThreshold)
        {
            _consecutiveFailures[key] = 0;
            NotifyWriteFailureAlarm(target.DeviceId, target.VariableKey, result.ErrorMessage ?? "未知原因");
        }
    }

    /// <summary>
    /// 补写扫描（根因 A3）：补写队列中超过退避期、且可为指定设备过滤的条目，成功移除失败保留（重置时间戳）。
    /// </summary>
    private async Task RetryPendingWritesAsync(int? deviceId = null)
    {
        if (_pendingWrites.IsEmpty)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var kv in _pendingWrites.ToArray())
        {
            var (devId, varKey) = kv.Key;
            if (deviceId.HasValue && devId != deviceId.Value)
            {
                continue;
            }
            if ((now - kv.Value.At).TotalMilliseconds < PendingRetryIntervalMs)
            {
                continue;
            }

            try
            {
                var target = new BindingTarget(devId, varKey, kv.Value.OutOfRangePolicy);
                var result = await TryWriteOnceAsync(target, kv.Value.Value!);
                if (result.Success)
                {
                    OnWriteSuccess(target, kv.Value.Value!);
                    _pendingWrites.TryRemove(kv.Key, out _);
                    _logger.LogInformation("变量绑定离线补写成功：{DeviceId}/{Key}", devId, varKey);
                }
                else
                {
                    _pendingWrites[kv.Key] = new PendingWrite(kv.Value.Value, kv.Value.OutOfRangePolicy, now);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "变量绑定离线补写异常：{DeviceId}/{Key}", devId, varKey);
            }
        }
    }

    /// <summary>瞬时可重试失败分类判定。</summary>
    private static bool IsRetryable(VariableWriteFailureKind kind) => kind switch
    {
        VariableWriteFailureKind.DeviceNotRunning => true,
        VariableWriteFailureKind.DriverNotReady => true,
        VariableWriteFailureKind.DeviceNotConnected => true,
        VariableWriteFailureKind.Timeout => true,
        VariableWriteFailureKind.DriverError => true,
        _ => false
    };

    /// <summary>
    /// 去抖 + 最小间隔的重载调度（根因 B1）：100ms 去抖吸收批量注册/重连，1s 最小间隔压降读库开销。
    /// </summary>
    private async Task ReloadDebouncedAsync()
    {
        try
        {
            await Task.Delay(100);
            var waitMs = 1000 - (int)(DateTime.UtcNow - _lastLoadUtc).TotalMilliseconds;
            if (waitMs > 0)
            {
                await Task.Delay(waitMs);
            }

            await LoadAsync();
            _lastLoadUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "变量绑定引擎调度重载失败。");
        }
        finally
        {
            Interlocked.Exchange(ref _reloadPending, 0);
        }
    }

    /// <summary>
    /// 检测绑定图中的自环与多节点环，标记并拒绝涉及环的绑定，返回「绑定名 → 环详情」映射（供调用方去重告警）。
    /// 以 (设备Id, 变量Key) 为节点、绑定为有向边构图，DFS 标记环上节点。
    /// </summary>
    private Dictionary<string, string> DetectAndRejectCycles(List<BindingCandidate> candidates)
    {
        var rejectedDetails = new Dictionary<string, string>();

        // 自环：源 == 目标
        foreach (var c in candidates)
        {
            if (c.SourceDeviceId == c.TargetDeviceId && c.SourceVariableKey == c.TargetVariableKey)
            {
                c.Rejected = true;
                rejectedDetails[c.Name] = $"{c.SourceDeviceId}/{c.SourceVariableKey} → 自身";
                _logger.LogError("变量绑定拒绝（自环）：{Name} {Dev}/{Key} → 自身", c.Name, c.SourceDeviceId, c.SourceVariableKey);
            }
        }

        // 构建邻接表（仅未拒绝候选）
        var adj = new Dictionary<(int, string), List<(int, string)>>();
        foreach (var c in candidates.Where(x => !x.Rejected))
        {
            var s = (c.SourceDeviceId, c.SourceVariableKey);
            var t = (c.TargetDeviceId, c.TargetVariableKey);
            if (!adj.TryGetValue(s, out var list))
            {
                list = new List<(int, string)>();
                adj[s] = list;
            }
            if (!list.Contains(t)) list.Add(t);
        }

        // DFS 检测环路，标记环上节点
        var inCycle = new HashSet<(int, string)>();
        var visited = new HashSet<(int, string)>();
        var onPath = new HashSet<(int, string)>();
        var stack = new List<(int, string)>();
        void Dfs((int, string) node)
        {
            if (onPath.Contains(node))
            {
                var idx = stack.IndexOf(node);
                for (var i = idx; i < stack.Count; i++) inCycle.Add(stack[i]);
                return;
            }
            if (visited.Contains(node)) return;
            visited.Add(node);
            onPath.Add(node);
            stack.Add(node);
            if (adj.TryGetValue(node, out var nexts))
                foreach (var n in nexts) Dfs(n);
            stack.RemoveAt(stack.Count - 1);
            onPath.Remove(node);
        }
        foreach (var n in adj.Keys) Dfs(n);

        // 拒绝涉及环上节点的绑定
        foreach (var c in candidates.Where(x => !x.Rejected))
        {
            if (inCycle.Contains((c.SourceDeviceId, c.SourceVariableKey)) || inCycle.Contains((c.TargetDeviceId, c.TargetVariableKey)))
            {
                c.Rejected = true;
                rejectedDetails[c.Name] = $"{c.SourceDeviceId}/{c.SourceVariableKey} → {c.TargetDeviceId}/{c.TargetVariableKey}";
                _logger.LogError("变量绑定拒绝（环路）：{Name} {SrcDev}/{SrcKey} → {TgtDev}/{TgtKey}",
                    c.Name, c.SourceDeviceId, c.SourceVariableKey, c.TargetDeviceId, c.TargetVariableKey);
            }
        }

        return rejectedDetails;
    }

    /// <summary>对新增的环路绑定推送系统报警（进程内按绑定名去重，仅集合变化才推送）。</summary>
    private void NotifyNewCycleAlarms(Dictionary<string, string> rejectedDetails)
    {
        var newCycleNames = new HashSet<string>(rejectedDetails.Keys);
        List<(string Name, string Detail)>? toAlarm = null;
        lock (_cycleAlarmGate)
        {
            foreach (var name in newCycleNames)
            {
                if (!_notifiedCycleKeys.Contains(name))
                {
                    (toAlarm ??= new List<(string, string)>()).Add((name, rejectedDetails[name]));
                }
            }
            _notifiedCycleKeys = newCycleNames;
        }

        if (toAlarm != null)
        {
            foreach (var (name, detail) in toAlarm)
            {
                NotifyCycleAlarm(name, detail);
            }
        }
    }

    /// <summary>推送绑定环路系统报警（fire-and-forget）。</summary>
    private void NotifyCycleAlarm(string name, string detail)
    {
        try
        {
            _ = _notificationService.NotifySystemAlarmAsync(0, string.Empty, name, $"变量绑定检测到环路，已拒绝加载：{detail}", "Error");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推送绑定环路报警失败：{Name}", name);
        }
    }

    /// <summary>推送变量绑定连续写入失败系统报警（fire-and-forget）。</summary>
    private void NotifyWriteFailureAlarm(int deviceId, string variableKey, string message)
    {
        try
        {
            _ = _notificationService.NotifySystemAlarmAsync(
                deviceId, variableKey, variableKey,
                $"变量绑定目标连续写入失败 {ConsecutiveFailureAlarmThreshold} 次：{message}", "Error");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推送变量绑定连续写入失败报警失败：{DeviceId}/{Key}", deviceId, variableKey);
        }
    }

    private sealed record BindingTarget(int DeviceId, string VariableKey, string OutOfRangePolicy);

    /// <summary>离线补写条目：最新值 + 越限策略 + 最近失败时间戳。</summary>
    private readonly record struct PendingWrite(object? Value, string OutOfRangePolicy, DateTime At);

    private sealed class BindingCandidate
    {
        public BindingCandidate(int sourceDeviceId, string sourceVariableKey, int targetDeviceId, string targetVariableKey, string name, string outOfRangePolicy)
        {
            SourceDeviceId = sourceDeviceId;
            SourceVariableKey = sourceVariableKey;
            TargetDeviceId = targetDeviceId;
            TargetVariableKey = targetVariableKey;
            Name = name;
            OutOfRangePolicy = outOfRangePolicy;
        }

        public int SourceDeviceId { get; }
        public string SourceVariableKey { get; }
        public int TargetDeviceId { get; }
        public string TargetVariableKey { get; }
        public string Name { get; }
        public string OutOfRangePolicy { get; }
        public bool Rejected { get; set; }
    }
}