using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Runtime;
using ScadaServer.Runtime.Events;

namespace ScadaServer.Runtime.Bindings;

/// <inheritdoc cref="IVariableBindingEngine"/>
/// <remarks>
/// 阶段二最小闭环：单向、OnChange、无转换的绑定转发。
/// 多跳链与环（A→B→A）的正确性与回声抑制属于阶段三范畴，本阶段仅做单跳且要求目标变量不被配置为其它绑定的源。
/// </remarks>
public sealed class VariableBindingEngine : IVariableBindingEngine
{
    private readonly IVariableChangeBus _changeBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VariableBindingEngine> _logger;

    // 绑定索引：(源设备Id, 源变量Key) -> 目标列表。volatile 赋值保证读线程立即可见。
    private volatile Dictionary<(int DeviceId, string VariableKey), List<BindingTarget>> _index = new();

    // 运行指标（Interlocked 更新），后续可接入健康检查/监控端点。
    private long _writeSuccess;
    private long _writeFail;

    public VariableBindingEngine(
        IVariableChangeBus changeBus,
        IServiceProvider serviceProvider,
        ILogger<VariableBindingEngine> logger)
    {
        _changeBus = changeBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _changeBus.VariableChanged += OnVariableChanged;
    }

    /// <inheritdoc/>
    public async Task LoadAsync()
    {
        // RM 为 Singleton，此处解析到的即为当前运行实例。
        var rm = _serviceProvider.GetRequiredService<RuntimeManager>();

        var newIndex = new Dictionary<(int, string), List<BindingTarget>>();

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

                var key = (c.SourceDeviceId, c.SourceVariableKey);
                if (!newIndex.TryGetValue(key, out var list))
                {
                    list = new List<BindingTarget>();
                    newIndex[key] = list;
                }
                list.Add(new BindingTarget(c.TargetDeviceId, c.TargetVariableKey));
            }
        }

        _index = newIndex;
        _logger.LogInformation("变量绑定引擎已加载 {Count} 条源映射。", newIndex.Count);
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
        // 跳过引擎自身写入产生的事件，避免即时回环（多跳链/环依赖阶段三环检测）。
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

    private sealed record BindingTarget(int DeviceId, string VariableKey);
}
