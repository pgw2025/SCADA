using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ScadaServer.Runtime.Devices
{
    /// <summary>
    /// 设备调度器，负责控制设备轮询频率和并发执行
    /// </summary>
    /// <remarks>
    /// 采用轮询式调度模式，以固定间隔遍历所有设备并分发工作线程。
    /// 通过 SemaphoreSlim 实现并发限流，防止过多设备同时访问导致资源耗尽。
    /// 每个设备的工作线程独立执行（fire-and-forget），调度器不等待单个设备完成即可继续调度下一个。
    /// </remarks>
    public class DeviceScheduler
    {
        private readonly RuntimeManager _runtimeManager;
        private readonly SemaphoreSlim _workerLimiter;
        private readonly ILogger<DeviceScheduler> _logger;
        private readonly ILogger<DeviceWorker> _workerLogger;

        /// <summary>
        /// 初始化设备调度器
        /// </summary>
        /// <param name="runtimeManager">运行时管理器，提供设备运行时列表</param>
        /// <param name="maxConcurrentWorkers">最大并发工作线程数，限制同时执行的设备任务数量</param>
        /// <param name="logger">日志记录器</param>
        public DeviceScheduler(RuntimeManager runtimeManager, int maxConcurrentWorkers, ILogger<DeviceScheduler> logger, ILogger<DeviceWorker> workerLogger)
        {
            _runtimeManager = runtimeManager;
            _workerLimiter = new SemaphoreSlim(maxConcurrentWorkers);
            _logger = logger;
            _workerLogger = workerLogger;
        }

        /// <summary>
        /// 启动调度器主循环
        /// </summary>
        /// <param name="token">取消令牌，用于停止调度器</param>
        /// <returns>任务完成时返回</returns>
        public async Task StartAsync(CancellationToken token)
        {
            _logger.LogInformation("DeviceScheduler started.");

            // 主调度循环，直到收到取消信号
            while (!token.IsCancellationRequested)
            {
                // 获取当前所有设备运行时的快照列表
                var devices = _runtimeManager.DeviceRuntimes.Values.ToList();

                // 遍历每个设备，分发工作线程
                foreach (var runtime in devices)
                {
                    // 获取信号量许可，限制并发数量
                    await _workerLimiter.WaitAsync(token);

                    // Fire-and-forget 模式启动设备工作线程
                    // 不等待工作完成，立即继续调度下一个设备
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var worker = new DeviceWorker(runtime, _workerLogger);
                            await worker.WorkerAsync(token);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "DeviceWorker for {DeviceKey} failed.", runtime.Device.Key);
                        }
                        finally
                        {
                            // 无论成功或失败，都必须释放信号量
                            // 确保不会因异常导致信号量泄漏和死锁
                            _workerLimiter.Release();
                        }
                    }, token);
                }

                // 调度器 tick 间隔，控制轮询频率
                // 50ms 间隔平衡调度精度和系统开销
                await Task.Delay(50, token);
            }

            _logger.LogInformation("DeviceScheduler stopped.");
        }
    }
}