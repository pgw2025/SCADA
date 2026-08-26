namespace ScadaServer.Runtime.Events;

/// <summary>
/// 进程内变量变化事件总线。Singleton 生命周期，跨采集循环与写入通道共享。
/// <para>发布为非阻塞操作：订阅者抛出的异常会被捕获并记日志，绝不影响采集循环或写入通道。</para>
/// </summary>
public interface IVariableChangeBus
{
    /// <summary>
    /// 变量变化事件。回调在发布线程上同步执行，订阅者应尽快返回、避免耗时操作。
    /// </summary>
    event EventHandler<VariableChangeEvent>? VariableChanged;

    /// <summary>
    /// 发布一次变量变化事件（非阻塞）。
    /// </summary>
    void Publish(VariableChangeEvent evt);
}
