using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;

namespace ScadaServer.Runtime.Devices
{
    /// <summary>
    /// 设备调度器，负责为每台设备派生唯一的常驻采集 Worker。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 派发模型（第九阶段后的修复）：
    /// 1. <b>单设备单 Worker</b>：每个设备在整个生命周期内至多一个活跃 Worker，
    ///    调度循环只对"尚无活跃 Worker"的设备派发，避免重复派发导致的历史重复入库、
    ///    变量值竞态与通知重复推送。
    /// 2. <b>取消并发信号量上限</b>：Worker 主体是异步等待（Task.Delay / 驱动异步读取），
    ///    不占用线程池线程。原 10 个许可的上限会在启用设备超过 10 台时，把后续设备
    ///    永久挡在门外（状态停留 Unknown → 前端显示离线）。改为每设备一个常驻 Worker 后
    ///    天然上限即为设备总数，无需外部闸门。
    /// 3. <b>自愈重拉</b>：Worker 意外退出（非取消）后，调度循环在退避窗口结束后重新派发。
    /// 4. 优雅关停：取消调度器全局 token，链接的 Worker 会同步收到取消并干净退出。
    /// </para>
    /// </remarks>
    public class DeviceScheduler
    {
        private readonly RuntimeManager _runtimeManager;
        private readonly ILogger<DeviceScheduler> _logger;
        private readonly ILogger<DeviceWorker> _workerLogger;
        private readonly IScadaNotificationService _notificationService;
        private readonly IHistoryRecorder _historyRecorder;

        // 调度器自身的取消源：由 StopAsync 触发，独立于宿主的 stoppingToken，
        // 确保在应用退出时调度循环与已派发的 worker 都能干净退出。
        private CancellationTokenSource? _cts;
        private readonly TaskCompletionSource _stoppedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // 每设备重拉退避表：key = 设备ID，value = 允许再次派发的最早时间点。
        // 防止 Worker 因不可恢复原因快速反复退出时，每个 tick 都疯狂重派刷屏。
        private readonly ConcurrentDictionary<int, DateTime> _retryAfter = new();

        private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 初始化设备调度器
        /// </summary>
        /// <param name="runtimeManager">运行时管理器，提供设备运行时列表</param>
        /// <param name="logger">调度器日志</param>
        /// <param name="workerLogger">Worker 日志</param>
        /// <param name="notificationService">设备/变量更新通知服务</param>
        /// <param name="historyRecorder">历史数据记录服务</param>
        public DeviceScheduler(
            RuntimeManager runtimeManager,
            ILogger<DeviceScheduler> logger,
            ILogger<DeviceWorker> workerLogger,
            IScadaNotificationService notificationService,
            IHistoryRecorder historyRecorder)
        {
            _runtimeManager = runtimeManager;
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
                // 主调度循环：每个 tick 只补发"尚无活跃 Worker 且已过退避窗口"的设备。
                while (!linkedToken.IsCancellationRequested)
                {
                    var devices = _runtimeManager.DeviceRuntimes.Values.ToList();

                    foreach (var runtime in devices)
                    {
                        // 已有活跃 Worker 的设备直接跳过，避免重复派发（同一时刻单设备单 Worker）。
                        if (runtime.IsRunning)
                        {
                            continue;
                        }

                        // 退避窗口内不重派，防止快速失败设备刷屏。
                        if (_retryAfter.TryGetValue(runtime.Device.Id, out var until) && DateTime.Now < until)
                        {
                            continue;
                        }

                        if (linkedToken.IsCancellationRequested)
                        {
                            break;
                        }

                        DispatchWorker(runtime, linkedToken);
                    }

                    if (linkedToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // 调度器 tick 间隔，控制派发检测频率：50ms 平衡灵敏度与系统开销。
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
        /// 为单台设备派生一个常驻采集 Worker（fire-and-forget，不等待完成）。
        /// </summary>
        /// <remarks>
        /// 派发前同步置位 <see cref="DeviceRuntime.IsRunning"/>，保证同一设备不会在两个 tick 间被重复派发；
        /// Worker 退出时在 finally 中复位该标志，并在"非取消退出"时登记重拉退避窗口。
        /// </remarks>
        private void DispatchWorker(DeviceRuntime runtime, CancellationToken linkedToken)
        {
            runtime.IsRunning = true;

            // 为当前设备派生独立取消令牌：链接全局关停令牌，同时支持运行期单设备注销/重载。
            var workerToken = runtime.CreateWorkerToken(linkedToken);

            runtime.WorkerTask = Task.Run(async () =>
            {
                try
                {
                    var worker = new DeviceWorker(
                        runtime, _workerLogger, _notificationService, _historyRecorder);
                    await worker.WorkerAsync(workerToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "DeviceWorker for {DeviceKey} failed.", runtime.Device.Key);
                }
                finally
                {
                    // 无论成功、异常还是取消，都必须复位标志，使调度器可再次派发。
                    runtime.IsRunning = false;

                    // 仅"非取消退出"需要安排重拉（取消代表正在关停该设备，不应自愈拉起）。
                    var cancelled = workerToken.IsCancellationRequested;
                    runtime.DisposeWorkerToken();
                    runtime.WorkerTask = null;

                    if (!cancelled)
                    {
                        _retryAfter[runtime.Device.Id] = DateTime.Now + RetryBackoff;
                    }
                }
            }, linkedToken);
        }

        /// <summary>
        /// 停止调度器主循环，触发所有活跃 Worker 收尾退出。
        /// </summary>
        public async Task StopAsync()
        {
            if (_cts == null)
            {
                return;
            }

            // 触发取消：调度循环与进行中的 worker 都会收到信号。
            _cts.Cancel();

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