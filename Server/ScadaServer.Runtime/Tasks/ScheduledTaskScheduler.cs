using System.Collections.Concurrent;
using System.Diagnostics;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Runtime.Tasks
{
    /// <summary>
    /// 定时任务调度器实现。
    /// <para>
    /// 调度模型（与 ScriptEngineHost 一致）：单一后台 Tick 循环（1 秒）驱动 Cron 触发；
    /// 防重入：同一任务上一次执行尚未结束时跳过本次触发（记 Skipped）；
    /// 执行状态（Running/Success/Failed/Skipped + 耗时/错误/下次触发时间）回写 ScheduledTasks 表供前端轮询。
    /// </para>
    /// </summary>
    public class ScheduledTaskScheduler : IScheduledTaskScheduler, IHostedService
    {
        /// <summary>Tick 间隔：任务最细粒度为秒级 Cron，1 秒轮询足够。</summary>
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

        /// <summary>兜底全量重载间隔：捕获 CRUD 通知丢失 / 库内被外部修改的情况。</summary>
        private static readonly TimeSpan ReloadInterval = TimeSpan.FromMinutes(10);

        /// <summary>Cron 调度时区（与脚本引擎一致，统一按北京时间）。</summary>
        private static readonly TimeZoneInfo ScheduleZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScheduledTaskScheduler> _logger;
        private readonly IEnumerable<IScheduledTaskExecutor> _executors;

        /// <summary>已加载的任务作业快照（key = ScheduledTask.Id）。</summary>
        private readonly ConcurrentDictionary<int, TaskJob> _jobs = new();

        /// <summary>正在执行中的任务 Id 集合（防同一任务并发重叠执行）。</summary>
        private readonly ConcurrentDictionary<int, byte> _inflight = new();

        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;

        public ScheduledTaskScheduler(
            IServiceScopeFactory scopeFactory,
            ILogger<ScheduledTaskScheduler> logger,
            IEnumerable<IScheduledTaskExecutor> executors)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _executors = executors;
        }

        /// <summary>记录任务调度快照的对象。</summary>
        private sealed class TaskJob
        {
            public ScheduledTask Task { get; init; } = null!;
            public DateTime? NextUtc { get; set; }
        }

        // =============== IHostedService ===============

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await ReloadAsync();
            // ScheduleLoopAsync 本身返回热 Task，无需 Task.Run；保存引用供 StopAsync 等待退出。
            _loopTask = ScheduleLoopAsync(_loopCts.Token);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _loopCts?.Cancel();
            if (_loopTask is not null)
            {
                try
                {
                    // 等待调度循环退出；超时兜底防止宿主关闭被拖死。
                    await _loopTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("定时任务调度循环停止超时。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "定时任务调度循环退出异常。");
                }
            }

            // 循环退出后等待在途执行收尾（超时放弃，避免宿主关闭被长时间任务拖死）。
            await WaitForInflightAsync(TimeSpan.FromSeconds(30));

            // 全部收尾后才 Dispose，避免循环仍在使用 token 时触发 ObjectDisposedException。
            _loopCts?.Dispose();
            _loopCts = null;
            _jobs.Clear();
            _inflight.Clear();
        }

        // =============== 加载与重载 ===============

        /// <inheritdoc/>
        public async Task ReloadAsync()
        {
            try
            {
                List<ScheduledTask> tasks;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
                    tasks = await repo.GetListAsync();
                }

                var now = DateTime.UtcNow;
                var next = new ConcurrentDictionary<int, TaskJob>();

                foreach (var t in tasks)
                {
                    var nextUtc = t.Active ? ComputeNextUtc(t.CronExpression, now) : null;

                    // NextRunAt 展示值与调度快照保持一致；仅变化时回写，避免每次重载都打库。
                    if (t.NextRunAt != nextUtc)
                    {
                        t.NextRunAt = nextUtc;
                        await PersistAsync(t);
                    }

                    if (t.Active && nextUtc.HasValue)
                    {
                        next[t.Id] = new TaskJob { Task = t, NextUtc = nextUtc };
                    }
                }

                _jobs.Clear();
                foreach (var (id, job) in next)
                {
                    _jobs[id] = job;
                }

                _logger.LogInformation("定时任务调度器重载完成：共 {Total} 项任务，{Scheduled} 项进入调度。",
                    tasks.Count, _jobs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时任务调度器重载失败，调度保持上一次状态。");
            }
        }

        /// <summary>
        /// 计算任务下一次触发时间（UTC）。Cron 兼容 6 段秒级与 5 段分钟级；不可解析时返回 null（不进入调度）。
        /// </summary>
        private static DateTime? ComputeNextUtc(string? cronExpression, DateTime nowUtc)
        {
            if (TryParseCron(cronExpression, out var cron))
            {
                try
                {
                    return cron.GetNextOccurrence(nowUtc, ScheduleZone);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>解析 Cron：先按 6 段秒级，失败再按 5 段分钟级（与 AppService 校验一致）。</summary>
        internal static bool TryParseCron(string? expression, out CronExpression cron)
        {
            cron = null!;
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }
            try
            {
                cron = CronExpression.Parse(expression, CronFormat.IncludeSeconds);
                return true;
            }
            catch (CronFormatException)
            {
                try
                {
                    cron = CronExpression.Parse(expression, CronFormat.Standard);
                    return true;
                }
                catch (CronFormatException)
                {
                    return false;
                }
            }
        }

        // =============== 调度循环 ===============

        private async Task ScheduleLoopAsync(CancellationToken token)
        {
            using var tick = new PeriodicTimer(TickInterval);
            var lastReload = DateTime.UtcNow;

            try
            {
                while (!token.IsCancellationRequested && await tick.WaitForNextTickAsync(token))
                {
                    var now = DateTime.UtcNow;

                    // 兜底周期重载：捕获 CRUD 通知丢失 / 外部改库。
                    if (now - lastReload >= ReloadInterval)
                    {
                        lastReload = now;
                        await ReloadAsync();
                        continue;
                    }

                    foreach (var job in _jobs.Values)
                    {
                        if (job.NextUtc == null || now < job.NextUtc.Value)
                        {
                            continue;
                        }

                        // 到期：先推进下一次触发时间（避免执行期间再次到期重复派发），再异步派发。
                        job.NextUtc = ComputeNextUtc(job.Task.CronExpression, now);
                        job.Task.NextRunAt = job.NextUtc;
                        DispatchFireAndTrack(job.Task, "Cron");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出路径
            }
            catch (Exception ex)
            {
                // 未预期异常不能让调度循环静默死亡（fire-and-forget 时代无法察觉）。
                _logger.LogError(ex, "定时任务调度循环因未预期异常退出。");
            }
        }

        /// <summary>
        /// 派发并跟踪：Cron 触发时不阻塞调度循环，但通过 _inflight 跟踪在途执行，
        /// 并对派发链路（含 DispatchAsync 内 catch 块中落库失败逃逸的异常）兜底捕获。
        /// </summary>
        private void DispatchFireAndTrack(ScheduledTask task, string triggerSource)
        {
            _ = DispatchSafeAsync(task, triggerSource);
        }

        private async Task DispatchSafeAsync(ScheduledTask task, string triggerSource)
        {
            try
            {
                await DispatchAsync(task, triggerSource);
            }
            catch (Exception ex)
            {
                // DispatchAsync 内部已捕获执行异常；此处兜底的是落库（PersistStatusAsync）等链路异常。
                _logger.LogError(ex, "定时任务 {Id} 派发链路异常（含执行结果落库失败）。", task.Id);
            }
        }

        /// <summary>停止时等待在途执行收尾；超时放弃并告警，避免宿主关闭被长时间任务拖死。</summary>
        private async Task WaitForInflightAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!_inflight.IsEmpty && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }

            if (!_inflight.IsEmpty)
            {
                _logger.LogWarning("停止时仍有 {Count} 个定时任务在途执行，放弃等待。", _inflight.Count);
            }
        }

        /// <summary>
        /// 派发执行：置 Running → 执行器执行 → 回写终态（Success/Failed/Skipped）。
        /// 由调度循环触发时为 fire-and-forget；手动触发时同步等待结果。
        /// </summary>
        private async Task<TaskRunResult> DispatchAsync(ScheduledTask task, string triggerSource)
        {
            var started = DateTime.UtcNow;

            // 防重入：同一任务上一次执行尚未结束则跳过。
            if (!_inflight.TryAdd(task.Id, 0))
            {
                var skipResult = new TaskRunResult
                {
                    TaskId = task.Id,
                    Status = "Skipped",
                    Error = "上一次执行尚未结束，本次触发被跳过。"
                };
                await PersistStatusAsync(task, "Skipped", started, 0, skipResult.Error, null);
                return skipResult;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                // 置 Running（前端轮询可见），同时带上已推进的 NextRunAt。
                await PersistStatusAsync(task, "Running", started, null, null, task.NextRunAt);

                var executor = _executors.FirstOrDefault(e => e.Type == task.Type);
                if (executor == null)
                {
                    throw new InvalidOperationException($"不支持的任务类型 '{task.Type}'（未注册对应执行器）");
                }

                string output;
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30)))
                {
                    output = await executor.ExecuteAsync(task, cts.Token);
                }

                sw.Stop();
                var success = new TaskRunResult
                {
                    TaskId = task.Id,
                    Status = "Success",
                    Output = output,
                    DurationMs = (int)sw.ElapsedMilliseconds
                };
                await PersistStatusAsync(task, "Success", started, success.DurationMs, null, null);
                _logger.LogInformation("定时任务 [{Name}]（{Trigger}）执行成功，耗时 {Ms}ms：{Output}",
                    task.Name, triggerSource, success.DurationMs, output);
                return success;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var error = ex.Message is { Length: > 2000 } ? ex.Message[..2000] : ex.Message;
                var failure = new TaskRunResult
                {
                    TaskId = task.Id,
                    Status = "Failed",
                    Error = error,
                    DurationMs = (int)sw.ElapsedMilliseconds
                };
                await PersistStatusAsync(task, "Failed", started, failure.DurationMs, error, null);
                _logger.LogWarning("定时任务 [{Name}]（{Trigger}）执行失败，耗时 {Ms}ms：{Error}",
                    task.Name, triggerSource, failure.DurationMs, error);
                return failure;
            }
            finally
            {
                _inflight.TryRemove(task.Id, out _);
            }
        }

        // =============== 公开执行接口 ===============

        /// <inheritdoc/>
        public async Task<TaskRunResult> RunAsync(int taskId, string executedBy)
        {
            // 从作业快照取最新，若不存在（如任务未启用）则回源库读（允许手动执行未启用任务）。
            ScheduledTask? task;
            if (_jobs.TryGetValue(taskId, out var job))
            {
                task = job.Task;
            }
            else
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
                task = await repo.GetByIdAsync(taskId);
            }

            if (task == null)
            {
                return new TaskRunResult { TaskId = taskId, Status = "Failed", Error = "任务不存在。" };
            }

            _logger.LogInformation("定时任务 [{Name}] 被 {User} 手动触发。", task.Name, executedBy);
            return await DispatchAsync(task, $"Manual:{executedBy}");
        }

        // =============== 持久化 ===============

        /// <summary>回写执行状态（含 LastRunAt/LastStatus/LastError/LastDurationMs，可选 NextRunAt）。</summary>
        private async Task PersistStatusAsync(
            ScheduledTask task, string status, DateTime started, int? durationMs, string? error, DateTime? nextRunAt)
        {
            task.LastRunAt = started;
            task.LastStatus = status;
            task.LastDurationMs = durationMs;
            task.LastError = status == "Success" ? null : error;
            if (nextRunAt.HasValue)
            {
                task.NextRunAt = nextRunAt;
            }
            await PersistAsync(task);
        }

        private async Task PersistAsync(ScheduledTask task)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
                await repo.UpdateAsync(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时任务状态持久化失败。TaskId={Id}", task.Id);
            }
        }
    }
}
