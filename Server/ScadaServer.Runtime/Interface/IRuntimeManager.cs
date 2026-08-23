namespace ScadaServer.Runtime.Interface;

/// <summary>
/// 运行时管理器接口
/// </summary>
public interface IRuntimeManager
{
    /// <summary>
    /// 初始化运行时
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// 启动运行时调度
    /// </summary>
    Task StartAsync(CancellationToken token, int maxConcurrentWorkers = 10);

    /// <summary>
    /// 停止运行时调度，优雅退出所有设备工作线程
    /// </summary>
    Task StopAsync();
}