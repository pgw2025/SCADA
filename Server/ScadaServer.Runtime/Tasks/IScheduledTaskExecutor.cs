using ScadaServer.Domain.Entities;

namespace ScadaServer.Runtime.Tasks
{
    /// <summary>
    /// 定时任务执行器（策略模式）：每种任务类型（TaskTypes 常量）一个实现，
    /// 由 <see cref="ScheduledTaskScheduler"/> 按 <see cref="ScheduledTask.Type"/> 分派。
    /// </summary>
    public interface IScheduledTaskExecutor
    {
        /// <summary>支持的任务类型（对应 <see cref="Domain.Enums.ScheduledTaskTypes"/> 常量）</summary>
        string Type { get; }

        /// <summary>
        /// 执行任务。成功返回输出摘要；失败抛出异常（由调度器统一捕获并记录 LastError）。
        /// </summary>
        Task<string> ExecuteAsync(ScheduledTask task, CancellationToken token);
    }
}
