using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Runtime.Alarms;
using ScadaServer.Runtime.Events;
using ScadaServer.Runtime.Processing;

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
        private readonly IVariableValueProcessor _valueProcessor;

        // 调度器自身的取消源：由 StopAsync 触发，独立于宿主的 stoppingToken，
        // 确保在应用退出时调度循环与已派发的 worker 都能干净退出。
        private CancellationTokenSource? _cts;
        private readonly TaskCompletionSource _stoppedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // 调度循环的热任务句柄：StartAsync 启动后立即返回，循环在后台运行，
        // 供 StopAsync 观察循环退出与异常。
        private Task? _loopTask;

        // 每设备重拉退避表：key = 设备ID，value = 允许再次派发的最早时间点。
        // 防止 Worker 因不可恢复原因快速反复退出时，每个 tick 都疯狂重派刷屏。
        private readonly ConcurrentDictionary<int, DateTime> _retryAfter = new();

        // 活跃 Worker 登记表：key = 设备ID，value = 已派发的 Worker 任务。
        // 派发时登记（新任务覆盖旧任务），供 StopAsync 聚合等待所有 Worker 收尾，
        // 确保驱动释放不会发生在 Worker 仍在使用驱动的期间。
        private readonly ConcurrentDictionary<int, Task> _workerTasks = new();

        private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 初始化设备调度器
        /// </summary>
        /// <param name="runtimeManager">运行时管理器，提供设备运行时列表</param>
        /// <param name="logger">调度器日志</param>
        /// <param name="workerLogger">Worker 日志</param>
        /// <param name="valueProcessor">变量值处理管线（Worker 构造传入，轮询与订阅共用）</param>
        public DeviceScheduler(
            RuntimeManager runtimeManager,
            ILogger<DeviceScheduler> logger,
            ILogger<DeviceWorker> workerLogger,
            IVariableValueProcessor valueProcessor)
        {
            _runtimeManager = runtimeManager;
            _logger = logger;
            _workerLogger = workerLogger;
            _valueProcessor = valueProcessor;
        }

        /// <summary>
        /// 启动调度器主循环（立即返回，循环在后台任务中运行）。
        /// </summary>
        /// <remarks>
        /// 与 VariableBindingEngine.StartAsync 保持同一模型：循环体作为热任务派发后立即返回，
        /// 避免调用方（RuntimeManager.StartAsync）被无限循环阻塞，
        /// 导致后续启动步骤（如变量绑定引擎）永远无法执行。
        /// </remarks>
        /// <param name="token">取消令牌，用于停止调度器（宿主关闭时触发）</param>
        /// <returns>启动完成即返回的任务</returns>
        public Task StartAsync(CancellationToken token)
        {
            // 用宿主 token 链接出调度器自身的 token，使 StopAsync 也能提前结束循环。
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            _logger.LogInformation("DeviceScheduler started.");

            // 循环体作为热任务在后台运行，不阻塞调用方。
            _loopTask = SchedulerLoopAsync(_cts.Token);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 调度器主循环：每个 tick 只补发"尚无活跃 Worker 且已过退避窗口"的设备。
        /// </summary>
        /// <param name="linkedToken">链接宿主 token 与调度器取消源的令牌</param>
        private async Task SchedulerLoopAsync(CancellationToken linkedToken)
        {
            try
            {
                while (!linkedToken.IsCancellationRequested)
                {
                    var devices = _runtimeManager.DeviceRuntimes.Values.ToList();

                    foreach (var runtime in devices)
                    {
                        // 待重连占位设备：不派发采集 Worker，按退避窗口触发运行时管理器重连。
                        if (runtime.NeedsReconnect)
                        {
                            if (_retryAfter.TryGetValue(runtime.Device.Id, out var reconnectUntil)
                                && DateTime.UtcNow < reconnectUntil)
                            {
                                continue;
                            }

                            // 先登记退避窗口再触发重连，防止重连在途时每个 tick 重复触发。
                            _retryAfter[runtime.Device.Id] = DateTime.UtcNow + RetryBackoff;
                            _ = ReconnectDeviceSafelyAsync(runtime.Device.Id);
                            continue;
                        }

                        // 已有活跃 Worker 的设备直接跳过，避免重复派发（同一时刻单设备单 Worker）。
                        if (runtime.IsRunning)
                        {
                            continue;
                        }

                        // 退避窗口内不重派，防止快速失败设备刷屏。
                        if (_retryAfter.TryGetValue(runtime.Device.Id, out var until) && DateTime.UtcNow < until)
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
            catch (Exception ex)
            {
                // 循环任务为 fire-and-forget，异常不会自然传播到调用方：在此兜底记录，
                // 防止成为未观察异常；_stoppedTcs 在 finally 中保证置位，StopAsync 不会悬挂。
                _logger.LogError(ex, "DeviceScheduler 主循环发生未预期异常，调度已停止。");
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
        /// 派发临界区（校验在册 → 置位 IsRunning → 创建 token → 记录任务）在
        /// <see cref="DeviceRuntime.DispatchSync"/> 锁内原子完成，与设备注销路径串行化：
        /// <list type="bullet">
        /// <item>设备已注销（不在册或已被新运行时替换）时不派发，杜绝僵尸 Worker；</item>
        /// <item>同一设备同一时刻至多一个 Worker；</item>
        /// <item>Worker 退出清理带所有权校验，不会误释放/覆盖新 Worker 的 token 与任务句柄。</item>
        /// </list>
        /// </remarks>
        private void DispatchWorker(DeviceRuntime runtime, CancellationToken linkedToken)
        {
            lock (runtime.DispatchSync)
            {
                // 设备已注销或已被新运行时替换：不再派发。
                if (!_runtimeManager.DeviceRuntimes.TryGetValue(runtime.Device.Id, out var current)
                    || !ReferenceEquals(current, runtime))
                {
                    return;
                }

                // 已有活跃 Worker：跳过（锁外快照检查的二次确认）。
                if (runtime.IsRunning)
                {
                    return;
                }

                runtime.IsRunning = true;

                // 为当前设备派生独立取消源：链接全局关停令牌，同时支持运行期单设备注销/重载。
                var workerCts = runtime.CreateWorkerCts(linkedToken);

                // Task.Run 不传入取消 token：避免 token 在任务开始执行前被取消时
                // 任务直接转为 Cancelled 状态、finally 不执行导致 IsRunning 永久卡 true。
                // Worker 内部自行监听 workerCts.Token 实现取消退出。
                var task = Task.Run(() => RunWorkerAsync(runtime, workerCts));

                runtime.WorkerTask = task;
                _workerTasks[runtime.Device.Id] = task;
            }
        }

        /// <summary>
        /// 单设备 Worker 主体：执行采集循环并在退出时做所有权安全的清理。
        /// </summary>
        private async Task RunWorkerAsync(DeviceRuntime runtime, CancellationTokenSource workerCts)
        {
            try
            {
                var worker = new DeviceWorker(runtime, _workerLogger, _valueProcessor);
                await worker.WorkerAsync(workerCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DeviceWorker for {DeviceKey} failed.", runtime.Device.Key);
            }
            finally
            {
                // 仅"非取消退出"需要安排重拉（取消代表正在关停该设备，不应自愈拉起）。
                var cancelled = workerCts.IsCancellationRequested;

                lock (runtime.DispatchSync)
                {
                    // 无论成功、异常还是取消，都必须复位标志，使调度器可再次派发。
                    runtime.IsRunning = false;

                    // 所有权校验：仅当取消源仍属于本 Worker 时才释放，
                    // 避免旧 Worker 退出清理误释放新 Worker 的取消源。
                    runtime.DisposeWorkerTokenIfCurrent(workerCts);
                }

                if (!cancelled)
                {
                    _retryAfter[runtime.Device.Id] = DateTime.UtcNow + RetryBackoff;
                }
            }
        }

        /// <summary>
        /// 触发设备自动重连（fire-and-forget 包装，异常就地记录，避免未观察异常）。
        /// </summary>
        private async Task ReconnectDeviceSafelyAsync(int deviceId)
        {
            try
            {
                await _runtimeManager.ReconnectDeviceAsync(deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设备 {DeviceId} 自动重连失败（退避窗口后将重试）。", deviceId);
            }
        }

        /// <summary>
        /// 停止调度器主循环，等待所有已派发 Worker 收尾退出。
        /// </summary>
        /// <remarks>
        /// 必须先等待全部 Worker 退出再返回：调用方（RuntimeManager.StopAsync）随后会
        /// 释放所有设备驱动，若 Worker 仍阻塞在驱动读取中，驱动会在其脚下被拆除。
        /// </remarks>
        public async Task StopAsync()
        {
            if (_cts == null)
            {
                return;
            }

            // 触发取消：调度循环与进行中的 worker 都会收到信号。
            _cts.Cancel();

            // 等待调度循环真正退出。
            try
            {
                await _stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("DeviceScheduler 调度循环停止超时（5s）。");
            }

            // 聚合等待所有已派发 Worker 收尾（含已注销设备的在途 Worker），
            // 确保驱动释放不会发生在 Worker 仍在使用驱动的期间。
            var workers = _workerTasks.Values.ToList();
            if (workers.Count > 0)
            {
                try
                {
                    await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("DeviceScheduler 等待 {Count} 个 Worker 退出超时（5s），部分 worker 可能仍在退出。",
                        workers.Count);
                }
                catch (Exception)
                {
                    // Worker 侧异常（含取消导致的 TaskCanceledException）均已各自记录，此处忽略聚合异常。
                }
            }

            _cts.Dispose();
            _cts = null;
        }
    }
}