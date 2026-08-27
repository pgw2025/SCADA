using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Runtime;
using ScadaServer.Runtime.Events;

namespace ScadaServer.Runtime.Bindings;

/// <inheritdoc cref="IVariableBindingEngine"/>
/// <remarks>
/// 阶段二+三：单向/多跳 OnChange 转发，含加载期环检测、回声加固与并发修正支持。
/// 环与自环在加载期拒绝；多跳链在防回环前提下支持；回声抑制依赖 VariableRuntime.LastBindingWriteValue。
/// </remarks>
public sealed class VariableBindingEngine : IVariableBindingEngine
{
    private readonly IVariableChangeBus _changeBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VariableBindingEngine> _logger;
    private readonly IScadaNotificationService _notificationService;

    // 绑定索引：(源设备Id, 源变量Key) -> 目标列表。volatile 赋值保证读线程立即可见。
    private volatile Dictionary<(int DeviceId, string VariableKey), List<BindingTarget>> _index = new();

    // 运行指标（Interlocked 更新），后续可接入健康检查/监控端点。
    private long _writeSuccess;
    private long _writeFail;

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

        var rejected = new List<string>();

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
                    continue;
                }
                if (!rm.DeviceRuntimes.TryGetValue(c.TargetDeviceId, out var tgtRt))
                {
                    _logger.LogWarning("变量绑定跳过：目标设备 {DeviceId} 未运行。绑定={Name}", c.TargetDeviceId, c.Name);
                    continue;
                }
                if (!srcRt.Variables.Values.Any(v => v.Key == c.SourceVariableKey))
                {
                    _logger.LogWarning("变量绑定跳过：源变量 {Key} 在设备 {DeviceId} 不存在。绑定={Name}", c.SourceVariableKey, c.SourceDeviceId, c.Name);
                    continue;
                }
                var tgtVar = tgtRt.Variables.Values.FirstOrDefault(v => v.Key == c.TargetVariableKey);
                if (tgtVar == null)
                {
                    _logger.LogWarning("变量绑定跳过：目标变量 {Key} 在设备 {DeviceId} 不存在。绑定={Name}", c.TargetVariableKey, c.TargetDeviceId, c.Name);
                    continue;
                }
                if (tgtVar.IsReadOnly)
                {
                    _logger.LogWarning("变量绑定跳过：目标变量 {Key} 为只读。绑定={Name}", c.TargetVariableKey, c.TargetDeviceId, c.Name);
                    continue;
                }

                candidates.Add(new BindingCandidate(
                    c.SourceDeviceId, c.SourceVariableKey,
                    c.TargetDeviceId, c.TargetVariableKey,
                    c.Name));
            }
        }

        // 2) 环检测（自环 + 多节点环），拒绝相关绑定。
        DetectAndRejectCycles(candidates, rejected);

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
            list.Add(new BindingTarget(cand.TargetDeviceId, cand.TargetVariableKey));
        }

        _index = newIndex;
        _logger.LogInformation("变量绑定引擎已加载 {Count} 条源映射，拒绝 {Rejected} 条（环路/非法）。", newIndex.Count, rejected.Count);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _index = new Dictionary<(int, string), List<BindingTarget>>();
    }

    /// <summary>
    /// 运行指标只读快照（成功/失败写入次数），供后续监控接入。
    /// </summary>
    public (long Success, long Fail) GetStats() =>
        (Interlocked.Read(ref _writeSuccess), Interlocked.Read(ref _writeFail));

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

        // 转发写入放到后台执行，避免阻塞采集循环/写入通道（事件总线为同步回调）。
        foreach (var t in targets)
        {
            _ = Task.Run(() => WriteTargetAsync(t, evt.Value));
        }
    }

    private async Task WriteTargetAsync(BindingTarget target, object? value)
    {
        if (value == null)
        {
            return;
        }

        try
        {
            var rm = _serviceProvider.GetRequiredService<RuntimeManager>();
            var result = await rm.WriteVariableAsync(target.DeviceId, target.VariableKey, value);
            if (result.Success)
            {
                Interlocked.Increment(ref _writeSuccess);

                // 回声加固：记录期望值与时间戳，供目标变量后续轮询回读时抑制回显事件。
                if (rm.DeviceRuntimes.TryGetValue(target.DeviceId, out var tr))
                {
                    var tv = tr.Variables.Values.FirstOrDefault(v => v.Key == target.VariableKey);
                    if (tv != null)
                    {
                        tv.LastBindingWriteValue = value;
                        tv.LastBindingWriteTime = DateTime.Now;
                    }
                }
            }
            else
            {
                Interlocked.Increment(ref _writeFail);
                _logger.LogWarning("变量绑定写入失败：{DeviceId}/{Key}：{Msg}", target.DeviceId, target.VariableKey, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeFail);
            _logger.LogError(ex, "变量绑定写入异常：{DeviceId}/{Key}", target.DeviceId, target.VariableKey);
        }
    }

    /// <summary>
    /// 检测绑定图中的自环与多节点环，标记并拒绝涉及环的绑定，推送系统报警。
    /// 以 (设备Id, 变量Key) 为节点、绑定为有向边构图，DFS 标记环上节点。
    /// </summary>
    private void DetectAndRejectCycles(List<BindingCandidate> candidates, List<string> rejected)
    {
        // 自环：源 == 目标
        foreach (var c in candidates)
        {
            if (c.SourceDeviceId == c.TargetDeviceId && c.SourceVariableKey == c.TargetVariableKey)
            {
                c.Rejected = true;
                rejected.Add(c.Name);
                _logger.LogError("变量绑定拒绝（自环）：{Name} {Dev}/{Key} → 自身", c.Name, c.SourceDeviceId, c.SourceVariableKey);
                NotifyCycleAlarm(c.Name, $"{c.SourceDeviceId}/{c.SourceVariableKey} → 自身");
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
                rejected.Add(c.Name);
                _logger.LogError("变量绑定拒绝（环路）：{Name} {SrcDev}/{SrcKey} → {TgtDev}/{TgtKey}",
                    c.Name, c.SourceDeviceId, c.SourceVariableKey, c.TargetDeviceId, c.TargetVariableKey);
                NotifyCycleAlarm(c.Name, $"{c.SourceDeviceId}/{c.SourceVariableKey} → {c.TargetDeviceId}/{c.TargetVariableKey}");
            }
        }
    }

    /// <summary>
    /// 推送绑定环路系统报警（fire-and-forget）。
    /// </summary>
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

    private sealed record BindingTarget(int DeviceId, string VariableKey);

    private sealed class BindingCandidate
    {
        public BindingCandidate(int sourceDeviceId, string sourceVariableKey, int targetDeviceId, string targetVariableKey, string name)
        {
            SourceDeviceId = sourceDeviceId;
            SourceVariableKey = sourceVariableKey;
            TargetDeviceId = targetDeviceId;
            TargetVariableKey = targetVariableKey;
            Name = name;
        }

        public int SourceDeviceId { get; }
        public string SourceVariableKey { get; }
        public int TargetDeviceId { get; }
        public string TargetVariableKey { get; }
        public string Name { get; }
        public bool Rejected { get; set; }
    }
}
