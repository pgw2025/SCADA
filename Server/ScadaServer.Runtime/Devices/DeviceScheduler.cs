using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;

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
        private readonly int _maxConcurrentWorkers;
        private readonly ILogger<DeviceScheduler> _logger;
        private readonly ILogger<DeviceWorker> _workerLogger;
        private readonly IScadaNotificationService _notificationService;
        private readonly IHistoryRecorder _historyRecorder;

        // 调度器自身的取消源：由 StopAsync 触发，独立于宿主的 stoppingToken，
        // 确保在应用退出时调度循环与已派发的 worker 都能干净退出。
        private CancellationTokenSource? _cts;
        private readonly TaskCompletionSource _stoppedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 初始化设备调度器
        /// </summary>
        /// <param name="runtimeManager">运行时管理器，提供设备运行时列表</param>
        /// <param name="maxConcurrentWorkers">最大并发工作线程数，限制同时执行的设备任务数量</param>
        /// <param name="logger">日志记录器</param>
        public DeviceScheduler(RuntimeManager runtimeManager, int maxConcurrentWorkers, ILogger<DeviceScheduler> logger, ILogger<DeviceWorker> workerLogger, IScadaNotificationService notificationService, IHistoryRecorder historyRecorder)
        {
            _runtimeManager = runtimeManager;
            _workerLimiter = new SemaphoreSlim(maxConcurrentWorkers);
            _maxConcurrentWorkers = maxConcurrentWorkers;
            _logger = logger;
            _workerLogger = workerLogger;
            _notificationService = notificationService;
            _historyRecorder = historyRecorder;
        }

        /// <summary>
        /// 启动调度器主循环
        /// </summary>
        /// <param name="token">取消令牌，用于停止调度器（宿主关闭时触发）</param>
        /// <returns>任务完成时返回</returns>
        public async Task StartAsync(CancellationToken token)
        {
            // 用宿主 token 链接出调度器自身的 token，使 StopAsync 也能提前结束循环。
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var linkedToken = _cts.Token;

            _logger.LogInformation("DeviceScheduler started.");

            try
            {
                // 主调度循环，直到收到取消信号
                while (!linkedToken.IsCancellationRequested)
                {
                    // 获取当前所有设备运行时的快照列表
                    var devices = _runtimeManager.DeviceRuntimes.Values.ToList();

                    // 遍历每个设备，分发工作线程
                    foreach (var runtime in devices)
                    {
                        // 获取信号量许可，限制并发数量。
                        // WaitAsync 可能因取消抛出 OperationCanceledException，
                        // 此时说明正在关闭，直接退出循环即可，不视为错误。
                        try
                        {
                            await _workerLimiter.WaitAsync(linkedToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        // Fire-and-forget 模式启动设备工作线程
                        // 不等待工作完成，立即继续调度下一个设备
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var worker = new DeviceWorker(runtime, _workerLogger, _notificationService, _historyRecorder);
                                await worker.WorkerAsync(linkedToken);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                _logger.LogError(ex, "DeviceWorker for {DeviceKey} failed.", runtime.Device.Key);
                            }
                            finally
                            {
                                // 无论成功或失败，都必须释放信号量
                                // 确保不会因异常导致信号量泄漏和死锁
                                _workerLimiter.Release();
                            }
                        }, linkedToken);
                    }

                    if (linkedToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // 调度器 tick 间隔，控制轮询频率
                    // 50ms 间隔平衡调度精度和系统开销
                    try
                    {
                        await Task.Delay(50, linkedToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                // 通知等待者：调度循环已退出。
                _stoppedTcs.TrySetResult();
                _logger.LogInformation("DeviceScheduler stopped.");
            }
        }

        /// <summary>
        /// 停止调度器主循环，并释放所有信号量许可（使仍在等待的 worker 不再阻塞）。
        /// </summary>
        public async Task StopAsync()
        {
            if (_cts == null)
            {
                return;
            }

            // 触发取消：正在 WaitAsync 的调度线程与进行中的 worker 都会收到信号。
            _cts.Cancel();

            // 将信号量计数一次性拉满到最大值，唤醒所有在 WaitAsync 上阻塞的派发循环，
            // 使其立即观察到取消信号并退出，避免 StopAsync 前已派发但尚未拿到许可的 worker 永久阻塞。
            // SemaphoreSlim.Release(int) 在 .NET 6+ 支持突发释放，超额会被忽略。
            try
            {
                _workerLimiter.Release(_maxConcurrentWorkers);
            }
            catch (SemaphoreFullException)
            {
                // 忽略：计数已达上限，说明没有阻塞者，无需处理。
            }

            // 等待调度循环真正退出，给在途 worker 一个收尾窗口。
            try
            {
                await _stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("DeviceScheduler 停止超时（5s），部分 worker 可能仍在退出。");
            }

            _cts.Dispose();
            _cts = null;
        }
    }
}