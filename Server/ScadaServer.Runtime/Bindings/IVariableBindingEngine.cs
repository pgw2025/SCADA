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

    /// <summary>
    /// 获取最近一次加载中被确定性跳过（变量不存在/目标只读等）规则的快照（规则Id → 跳过原因）。
    /// 供规则列表接口展示"未生效（原因）"状态。
    /// </summary>
    IReadOnlyDictionary<int, string> GetSkipReasons();

    /// <summary>
    /// 获取最近一次加载中进入 pending（设备未就绪，等待就绪后重试）规则的快照（规则Id → 等待原因）。
    /// </summary>
    IReadOnlyDictionary<int, string> GetPendingReasons();

    /// <summary>
    /// 调度一次「去抖 + 最小间隔」的绑定重载（根因 B）：用于设备注册/恢复就绪后补加载此前被跳过（pending）的规则。
    /// 100ms 去抖吸收批量注册，1s 最小间隔压降全量读库开销。内部异步执行，不等待结果。
    /// </summary>
    void ScheduleReload();
}
