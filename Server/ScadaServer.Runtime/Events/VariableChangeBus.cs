using System;
using Microsoft.Extensions.Logging;

namespace ScadaServer.Runtime.Events;

/// <inheritdoc cref="IVariableChangeBus"/>
public sealed class VariableChangeBus : IVariableChangeBus
{
    private readonly ILogger<VariableChangeBus> _logger;

    public VariableChangeBus(ILogger<VariableChangeBus> logger)
    {
        _logger = logger;
    }

    public event EventHandler<VariableChangeEvent>? VariableChanged;

    public void Publish(VariableChangeEvent evt)
    {
        // 快照调用列表，避免订阅集合在迭代期间变更导致异常；
        // 逐订阅者捕获异常，保证单个订阅者失败不影响其余订阅者与发布方（采集循环）。
        var handlers = VariableChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (EventHandler<VariableChangeEvent> handler in handlers.GetInvocationList())
        {
            try
            {
                handler.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "变量变化事件订阅者处理失败（已忽略，不影响采集）。DeviceId={DeviceId}, VarKey={VarKey}",
                    evt.DeviceId, evt.VariableKey);
            }
        }
    }
}
