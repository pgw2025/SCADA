using System.Collections.Concurrent;
using System.Diagnostics;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Runtime.Events;

namespace ScadaServer.Runtime.Scripting
{
    /// <summary>
    /// 脚本引擎宿主实现。
    /// <para>
    /// 调度模型：单一后台 Tick 循环（约 250ms），统一驱动 周期(Periodic) 与 Cron(Schedule)；
    /// 变量变化(OnChange) 通过订阅 <see cref="IVariableChangeBus"/> 驱动。避免每脚本一个 Timer，重载简单。
    /// 熔断：连续失败达阈值（默认 3）置 <see cref="SystemScript.Tripped"/>，调度跳过；手动 Run 可绕过。
    /// </para>
    /// </summary>
    public class ScriptEngineHost : IScriptEngineHost, IHostedService
    {
        /// <summary>熔断连续失败阈值。</summary>
        public const int CircuitBreakerThreshold = 3;

        private static readonly TimeZoneInfo ScheduleZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RuntimeManager _runtime;
        private readonly IVariableChangeBus _changeBus;
        private readonly IScadaNotificationService _notificationService;
        private readonly ILogger<ScriptEngineHost> _logger;
        private readonly ScriptRuntimeAccess _access;

        /// <summary>已加载的脚本作业快照（key = ScriptId）。</summary>
        private readonly ConcurrentDictionary<int, ScriptJob> _jobs = new();

        /// <summary>正在执行中的脚本 Id 集合（防同一脚本并发重叠执行）。</summary>
        private readonly ConcurrentDictionary<int, byte> _inflight = new();

        /// <summary>周期性/Cron 调度轮询用 CancellationTokenSource。</summary>
        private CancellationTokenSource? _loopCts;

        public ScriptEngineHost(
            IServiceScopeFactory scopeFactory,
            RuntimeManager runtime,
            IVariableChangeBus changeBus,
            IScadaNotificationService notificationService,
            ILogger<ScriptEngineHost> logger)
        {
            _scopeFactory = scopeFactory;
            _runtime = runtime;
            _changeBus = changeBus;
            _notificationService = notificationService;
            _logger = logger;
            _access = new ScriptRuntimeAccess(runtime);
        }

        // =============== IHostedService ===============

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // 订阅变量变化（OnChange 触发）。订阅一次即可，重载只更新作业快照。
            _changeBus.VariableChanged += OnVariableChanged;

            await ReloadAsync();
            _ = Task.Run(() => ScheduleLoopAsync(_loopCts.Token), _loopCts.Token);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _changeBus.VariableChanged -= OnVariableChanged;
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
            _jobs.Clear();
            _inflight.Clear();
            return Task.CompletedTask;
        }

        // =============== 加载与重载 ===============

        /// <inheritdoc/>
        public async Task ReloadAsync()
        {
            try
            {
                List<SystemScript> scripts;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<ISystemScriptRepository>();
                    scripts = await repo.GetListAsync();
                }

                var next = new ConcurrentDictionary<int, ScriptJob>();
                var now = DateTime.UtcNow;

                // 仅启用 + 未熔断的脚本进入调度
                foreach (var s in scripts.Where(s => s.Active && !s.Tripped))
                {
                    next[s.Id] = new ScriptJob
                    {
                        Script = s,
                        NextUtc = ComputeNextRuntime(s, now)
                    };
                }

                _jobs.Clear();
                foreach (var (id, job) in next)
                {
                    _jobs[id] = job;
                }

                _logger.LogInformation("脚本引擎重载完成：共 {Total} 项启用脚本，{Scheduled} 项进入调度。",
                    scripts.Count, _jobs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本引擎重载失败，调度保持上一次状态。");
            }
        }

        /// <summary>
        /// 计算脚本下一次应触发的时间（UTC）。Manual 不参与调度；Periodic 用当前+间隔；Schedule 用 Cron 下一匹配。
        /// </summary>
        private static DateTime? ComputeNextRuntime(SystemScript s, DateTime nowUtc)
        {
            if (s.TriggerType == ScriptTriggerType.Manual.ToString())
            {
                return null;
            }
            if (s.TriggerType == ScriptTriggerType.Periodic.ToString())
            {
                var seconds = s.IntervalSeconds ?? 1;
                return nowUtc.AddSeconds(seconds);
            }
            if (s.TriggerType == ScriptTriggerType.Schedule.ToString())
            {
                try
                {
                    var cron = CronExpression.Parse(s.CronExpression ?? string.Empty);
                    // 传入 UTC 的 DateTime，Cronos 返回同 UtcKind 的下一次触发时间。
                    return cron.GetNextOccurrence(nowUtc, ScheduleZone);
                }
                catch
                {
                    return null;
                }
            }
            // OnChange 由事件驱动，不做时间调度。
            return null;
        }

        /// <summary>记录脚本状态快照的对象。</summary>
        private sealed class ScriptJob
        {
            public SystemScript Script { get; init; } = null!;
            public DateTime? NextUtc { get; set; }
            public DateTime? LastChangeAtUtc { get; set; }
        }

        // =============== 调度循环 ===============

        private async Task ScheduleLoopAsync(CancellationToken token)
        {
            using var tick = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (!token.IsCancellationRequested && await tick.WaitForNextTickAsync(token))
            {
                var now = DateTime.UtcNow;
                foreach (var job in _jobs.Values)
                {
                    if (job.Script.TriggerType == ScriptTriggerType.Manual.ToString())
                    {
                        continue;
                    }

                    if (string.Equals(job.Script.TriggerType, ScriptTriggerType.OnChange.ToString()))
                    {
                        continue; // OnChange 由事件驱动
                    }

                    if (!job.Script.Active || job.Script.Tripped)
                    {
                        continue;
                    }

                    if (job.NextUtc == null)
                    {
                        continue;
                    }

                    if (now >= job.NextUtc.Value)
                    {
                        var dueType = job.Script.TriggerType;
                        job.NextUtc = ComputeNextRuntime(job.Script, now);
                        _ = DispatchAsync(job.Script, dueType, null, null);
                    }
                }
            }
        }

        /// <summary>
        /// 派发执行（不阻塞调用方），返回可能等待执行完成的结果；主要用于 Manual/Test 同步等待。
        /// </summary>
        private async Task<ScriptEngineResult?> DispatchAsync(
            SystemScript script,
            string triggerType,
            string? deviceContextKey,
            string? variableContextKey,
            string? executedBy = null,
            ScriptSandbox.TriggerPayload? payload = null)
        {
            var started = DateTime.UtcNow;

            // 并发保护：同一脚本上一次执行尚未结束则跳过（Skipped 不计入熔断失败计数）。
            if (!_inflight.TryAdd(script.Id, 0))
            {
                await PersistRecordAsync(script, triggerType, "Skipped", started, 0, null,
                    "[BUSY] 上一次执行尚未结束，本次触发被跳过", executedBy);
                return new ScriptEngineResult
                {
                    ScriptId = script.Id,
                    ScriptVersion = script.Version,
                    Result = "Skipped",
                    Error = "上一次执行尚未结束。"
                };
            }

            try
            {
                // 熔断检查：自动触发且已熔断则跳过（记 Skipped）；手动 Run 在入口已绕过。
                if (script.Tripped && triggerType != "Manual" && triggerType != "Test")
                {
                    await PersistRecordAsync(script, triggerType, "Skipped", started, 0, null, "[TRIPPED] 脚本已熔断，本次被跳过", executedBy);
                    return null;
                }

                var sw = Stopwatch.StartNew();
                var result = new ScriptEngineResult
                {
                    ScriptId = script.Id,
                    ScriptVersion = script.Version,
                    Result = "Success"
                };
                string output;

                try
                {
                    var sandbox = new ScriptSandbox(script.Code, ClampTimeout(script.TimeoutMs), _access,
                        triggerType == "Test", script.ScopeRead, script.ScopeWrite);
                    if (payload != null)
                    {
                        output = sandbox.OnChange(payload);
                    }
                    else
                    {
                        output = sandbox.Run();
                    }
                    result.Output = output;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    var isTimeout = ex is TimeoutException
                        || (ex.Message?.Contains("timeout", StringComparison.OrdinalIgnoreCase) ?? false);
                    result.Result = isTimeout ? "Timeout" : "Error";
                    result.Error = ex.Message;
                    result.Output = string.Empty;

                    await RecordFailureAsync(script, triggerType, started, sw.ElapsedMilliseconds, ex.Message, executedBy);
                    await TransmitAsync(result, triggerType, executedBy, wroteLog: false);
                    return result;
                }

                sw.Stop();
                result.DurationMs = (int)sw.ElapsedMilliseconds;

                // 成功：清零失败计数并刷新最近执行元信息。
                if (script.FailureCount != 0 || script.LastExecutedAt == null)
                {
                    script.FailureCount = 0;
                    script.LastError = null;
                    script.LastExecutedAt = started;
                    script.LastDurationMs = result.DurationMs;
                    await PersistScriptStatusAsync(script);
                }

                // 落执行记录
                var persisted = await PersistRecordAsync(script, triggerType, "Success", started, result.DurationMs, null, output, executedBy);
                result.WroteLog = persisted;
                await TransmitAsync(result, triggerType, executedBy, result.WroteLog);

                return result;
            }
            finally
            {
                _inflight.TryRemove(script.Id, out _);
            }
        }

        /// <summary>
        /// 异常/超时后更新失败的脚本（熔断计数、熔断标记、最近错误），并记录执行记录。
        /// </summary>
        private async Task RecordFailureAsync(SystemScript script, string triggerType, DateTime started, long durationMs, string error, string? executedBy)
        {
            string msg = error == null ? string.Empty : error.Length > 2000 ? error[..2000] : error;

            script.FailureCount += 1;
            script.LastError = msg;
            script.LastExecutedAt = started;
            script.LastDurationMs = (int)durationMs;
            if (script.FailureCount >= CircuitBreakerThreshold)
            {
                script.Tripped = true;
            }
            await PersistScriptStatusAsync(script);

            await PersistRecordAsync(script, triggerType, "Error", started, (int)durationMs, msg, null, executedBy);
        }

        // =============== 公开执行接口 ===============

        /// <inheritdoc/>
        public async Task<ScriptEngineResult> RunAsync(int scriptId, string executedBy)
        {
            // 从作业快照取最新，若不存在则回源库读（手动 Run 允许熔断态脚本）。
            var script = _jobs.TryGetValue(scriptId, out var job) ? job.Script : await LoadScriptByIdAsync(scriptId);
            if (script == null)
            {
                return new ScriptEngineResult { ScriptId = scriptId, Result = "Error", Error = "脚本不存在。" };
            }

            var r = await DispatchAsync(script, "Manual", null, null, executedBy);
            return r ?? new ScriptEngineResult { ScriptId = scriptId, Result = "Skipped", Error = "未能执行。" };
        }

        /// <inheritdoc/>
        public async Task<ScriptEngineResult> TestAsync(SystemScript script, string? deviceContextKey, string? variableContextKey, string executedBy)
        {
            var payload = (deviceContextKey != null && variableContextKey != null)
                ? new ScriptSandbox.TriggerPayload
                {
                    DeviceKey = deviceContextKey,
                    VariableKey = variableContextKey,
                    Value = _access.Read(deviceContextKey, variableContextKey),
                    PreviousValue = null,
                    Quality = _access.GetQuality(deviceContextKey, variableContextKey)
                }
                : null;

            var r = await DispatchAsync(script, "Test", deviceContextKey, variableContextKey, executedBy, payload);
            return r ?? new ScriptEngineResult { ScriptId = script.Id, Result = "Error", Error = "未能执行。" };
        }

        // =============== OnChange ===============

        private void OnVariableChanged(object? sender, VariableChangeEvent e)
        {
            // 解析设备键
            string deviceKey;
            if (_runtime.DeviceRuntimes.TryGetValue(e.DeviceId, out var runtime))
            {
                deviceKey = runtime.Device.Key;
            }
            else
            {
                return;
            }

            foreach (var job in _jobs.Values)
            {
                var s = job.Script;
                if (s.TriggerType != ScriptTriggerType.OnChange.ToString() || !s.Active || s.Tripped)
                {
                    continue;
                }
                if (!string.Equals(s.WatchDeviceKey, deviceKey, StringComparison.Ordinal)
                    || !string.Equals(s.WatchVariableKey, e.VariableKey, StringComparison.Ordinal))
                {
                    continue;
                }

                // 死区：|new - old| > DeadBand 才触发
                if (s.DeadBand.HasValue && s.DeadBand.Value > 0
                    && e.PreviousValue != null && e.Value != null
                    && TryToDouble(e.PreviousValue, out var prev) && TryToDouble(e.Value, out var cur))
                {
                    if (Math.Abs(cur - prev) <= s.DeadBand.Value)
                    {
                        continue;
                    }
                }

                // 冷却：抑制高频抖动与回声
                var now = DateTime.UtcNow;
                if (job.LastChangeAtUtc.HasValue && (now - job.LastChangeAtUtc.Value).TotalMilliseconds < s.CooldownMs)
                {
                    continue;
                }
                job.LastChangeAtUtc = now;

                var payload = new ScriptSandbox.TriggerPayload
                {
                    DeviceKey = deviceKey,
                    VariableKey = e.VariableKey,
                    Value = e.Value,
                    PreviousValue = e.PreviousValue,
                    Quality = e.Quality.ToString()
                };
                _ = DispatchAsync(s, "OnChange", deviceKey, e.VariableKey, executedBy: null, payload);
            }
        }

        // =============== 持久化与推送 ===============

        private async Task<bool> PersistRecordAsync(
            SystemScript script, string triggerType, string result,
            DateTime started, int? durationMs, string? error, string? output, string? executedBy)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IScriptExecutionRecordRepository>();
                var record = new ScriptExecutionRecord
                {
                    ScriptId = script.Id,
                    ScriptVersion = script.Version,
                    TriggerSource = triggerType,
                    Result = result,
                    StartedAt = started,
                    DurationMs = durationMs,
                    Error = error == null ? null : (error.Length > 4000 ? error[..4000] : error),
                    Output = output == null ? null : (output.Length > 8000 ? output[..8000] : output),
                    ExecutedBy = executedBy
                };
                await repo.InsertAsync(record);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本执行记录落库失败。ScriptId={Id}", script.Id);
                return false;
            }
        }

        private async Task PersistScriptStatusAsync(SystemScript script)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ISystemScriptRepository>();
                await repo.UpdateAsync(script);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本状态持久化失败。ScriptId={Id}", script.Id);
            }
        }

        private async Task TransmitAsync(ScriptEngineResult result, string triggerType, string? executedBy, bool wroteLog)
        {
            try
            {
                await _notificationService.NotifyScriptExecutionAsync(new ScriptExecutionEvent
                {
                    ScriptId = result.ScriptId,
                    ScriptVersion = result.ScriptVersion,
                    TriggerSource = triggerType,
                    Result = result.Result,
                    StartedAt = DateTime.UtcNow,
                    DurationMs = result.DurationMs,
                    Error = result.Error,
                    Output = result.Output,
                    ExecutedBy = executedBy
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "脚本执行事件推送失败。ScriptId={Id}", result.ScriptId);
            }
        }

        /// <summary>
        /// 从作业快照加载最近脚本；快照缺失时回源库读取。
        /// </summary>
        private async Task<SystemScript?> LoadScriptByIdAsync(int scriptId)
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISystemScriptRepository>();
            return await repo.GetByIdAsync(scriptId);
        }

        private static int ClampTimeout(int timeoutMs) => Math.Clamp(timeoutMs, 500, 30000);

        private static bool TryToDouble(object value, out double result)
        {
            try
            {
                result = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }
    }
}