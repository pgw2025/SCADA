using System.Threading;
using System.Threading.Tasks;

namespace ScadaServer.Runtime.Bindings;

/// <summary>
/// 变量绑定引擎接口（进程内 Singleton）。
/// 订阅变量变化事件总线，将源变量变化按配置转发写入目标变量。
/// </summary>
public interface IVariableBindingEngine
{
    /// <summary>
    /// 从数据库重新加载绑定索引（全量重建）。应在所有设备注册完成后调用。
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// 启动转发写入消费循环（应在 LoadAsync 之后调用）。
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 停止消费循环并排空待转发写入（运行时停止时调用）。
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 清空绑定索引（运行时停止时调用）。
    /// </summary>
    void Clear();
}
