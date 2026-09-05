using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Alarms;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Alarms;
using ScadaServer.Runtime.DataConversion;
using ScadaServer.Runtime.Devices;
using ScadaServer.Runtime.Events;

namespace ScadaServer.Runtime.Processing
{
    /// <summary>
    /// 变量值处理管线实现（决策 D2）。轮询与订阅两个数据源共用同一套下游处理：
    /// 工程换算 → 锁内内存更新 + 回声抑制 + IsChanged → changeBus 变化事件 →
    /// 通知入队（每设备有界通道泵）→ 历史/实时/报警。
    /// <para>
    /// 报警求值状态（越界去重/规则防抖）已迁移至 <see cref="DeviceRuntime"/>，本处理器持
    /// <see cref="DeviceRuntime.AlarmSync"/> 串行化求值；临界区内仅内存操作（通知与落库均为
    /// fire-and-forget），不做任何网络 IO。
    /// </para>
    /// </summary>
    public sealed class VariableValueProcessor : IVariableValueProcessor
    {
        /// <summary>通知通道容量：2048 条（高频订阅场景下的合理缓冲），满则丢最旧（DropOldest）。</summary>
        private const int NotificationCapacity = 2048;

        /// <summary>溢出丢弃计数打点间隔：每 100 条记一次 Warning，避免刷屏。</summary>
        private const int DropLogInterval = 100;

        private readonly IScadaNotificationService _notificationService;
        private readonly IHistoryRecorder _historyRecorder;
        private readonly IVariableChangeBus _changeBus;
        private readonly IAlarmRuleEngine _alarmRuleEngine;
        private readonly IAlarmRecorder _alarmRecorder;
        private readonly IRealtimeSnapshotService _realtimeSnapshot;
        private readonly ILogger<VariableValueProcessor> _logger;

        /// <summary>每设备通知队列：key = deviceId。设备注销经 <see cref="StopDevice"/> 移除。</summary>
        private readonly ConcurrentDictionary<int, DeviceNotificationQueue> _queues = new();

        public VariableValueProcessor(
            IScadaNotificationService notificationService,
            IHistoryRecorder historyRecorder,
            IVariableChangeBus changeBus,
            IAlarmRuleEngine alarmRuleEngine,
            IAlarmRecorder alarmRecorder,
            IRealtimeSnapshotService realtimeSnapshot,
            ILogger<VariableValueProcessor> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _historyRecorder = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
            _changeBus = changeBus ?? throw new ArgumentNullException(nameof(changeBus));
            _alarmRuleEngine = alarmRuleEngine ?? throw new ArgumentNullException(nameof(alarmRuleEngine));
            _alarmRecorder = alarmRecorder ?? throw new ArgumentNullException(nameof(alarmRecorder));
            _realtimeSnapshot = realtimeSnapshot ?? throw new ArgumentNullException(nameof(realtimeSnapshot));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task ApplyPolledAsync(DeviceRuntime runtime, VariableRuntime vr, object? rawValue, DateTime now)
        {
            var previousQuality = vr.Quality;

            // 工程换算（raw → engineering）：表达式为空即恒等，求值失败保持原始值。
            var newValue = VariableScaling.ToEngineering(vr, rawValue);

            // 无效值路径：null（含批读错误标记映射、驱动返回 null）→ CommunicationError，
            // 质量降级（好→坏）推送一次，值保持最近一次有效值（僵尸值）。
            if (newValue == null)
            {
                vr.Quality = VariableQuality.CommunicationError;
                if (previousQuality != VariableQuality.CommunicationError)
                {
                    EnqueueNotification(runtime, vr, vr.Value, vr.Quality, now);
                }
                return;
            }

            // 锁内内存更新：与 WriteVariableAsync（绑定写入/用户写入）临界区串行化，消除并发读写竞态。
            await runtime.Lock.WaitAsync();
            try
            {
                vr.PreviousValue = vr.Value;
                vr.Value = newValue;
                vr.UpdateTime = now;
                vr.Quality = VariableQuality.Good;

                // 回声抑制：若本次回读值等于绑定引擎最近一次写入的期望值且落在窗口内，
                // 视为绑定写入的回显，不发布变化事件。
                // 窗口公式兼容订阅模式：PollingIntervalMs 在订阅语义下为服务端采样/发布间隔。
                var echoWindowMs = Math.Max(vr.PollingIntervalMs, 1000) * 2;
                var isEcho = vr.LastBindingWriteValue != null
                             && (now - vr.LastBindingWriteTime).TotalMilliseconds <= echoWindowMs
                             && ValueEquals(newValue, vr.LastBindingWriteValue);

                vr.IsChanged = !Equals(vr.Value, vr.PreviousValue) && !isEcho;
            }
            finally
            {
                runtime.Lock.Release();
            }

            // 发布进程内变量变化事件（非阻塞），供绑定引擎等订阅者消费。
            if (vr.IsChanged && vr.Value != null)
            {
                _changeBus.Publish(new VariableChangeEvent
                {
                    DeviceId = runtime.Device.Id,
                    VariableKey = vr.Key,
                    Value = vr.Value,
                    PreviousValue = vr.PreviousValue,
                    Quality = vr.Quality,
                    UpdateTime = vr.UpdateTime,
                    Source = VariableChangeSource.Polling
                });
            }

            // 值变化或质量跃迁（坏→好恢复）都推送：质量恢复时即使值未变，前端也需要清除"通讯异常"标记。
            if (vr.IsChanged || previousQuality != VariableQuality.Good)
            {
                EnqueueNotification(runtime, vr, vr.Value, vr.Quality, vr.UpdateTime);
            }

            // 按变量存储策略记录历史采样点 / 更新实时快照 / 检测上下限越界与规则报警。
            // 三者为下游旁路：单段失败仅记日志，不影响值更新主链路与其余变量。
            TryRecordHistory(runtime, vr, now);
            TryUpdateRealtime(runtime, vr);
            TryCheckAlarm(runtime, vr);
        }

        /// <inheritdoc/>
        public async Task ApplySubscribedAsync(DeviceRuntime runtime, VariableRuntime vr, object? value, VariableQuality quality, DateTime now)
        {
            var previousQuality = vr.Quality;

            // 非 Good 质量（Bad/Uncertain）：走质量降级分支——值丢弃（保留最近有效值=僵尸值），
            // 统一置 CommunicationError（与轮询无效值路径语义一致，前端据此标记"通讯异常"），
            // 仅在 Good→降级跃迁时推送一次。
            if (quality != VariableQuality.Good)
            {
                vr.Quality = VariableQuality.CommunicationError;
                if (previousQuality != VariableQuality.CommunicationError)
                {
                    EnqueueNotification(runtime, vr, vr.Value, vr.Quality, now);
                }
                return;
            }

            // Good：与轮询成功路径完全一致（锁内更新 → 事件 → 通知 → 历史/实时/报警）。
            await runtime.Lock.WaitAsync();
            try
            {
                vr.PreviousValue = vr.Value;
                vr.Value = value;
                vr.UpdateTime = now;
                vr.Quality = VariableQuality.Good;

                // 回声抑制窗口公式与轮询一致（订阅语义下 PollingIntervalMs = 采样间隔）。
                var echoWindowMs = Math.Max(vr.PollingIntervalMs, 1000) * 2;
                var isEcho = vr.LastBindingWriteValue != null
                             && (now - vr.LastBindingWriteTime).TotalMilliseconds <= echoWindowMs
                             && ValueEquals(value, vr.LastBindingWriteValue);

                vr.IsChanged = !Equals(vr.Value, vr.PreviousValue) && !isEcho;
            }
            finally
            {
                runtime.Lock.Release();
            }

            if (vr.IsChanged && vr.Value != null)
            {
                _changeBus.Publish(new VariableChangeEvent
                {
                    DeviceId = runtime.Device.Id,
                    VariableKey = vr.Key,
                    Value = vr.Value,
                    PreviousValue = vr.PreviousValue,
                    Quality = vr.Quality,
                    UpdateTime = vr.UpdateTime,
                    Source = VariableChangeSource.Subscription
                });
            }

            if (vr.IsChanged || previousQuality != VariableQuality.Good)
            {
                EnqueueNotification(runtime, vr, vr.Value, vr.Quality, vr.UpdateTime);
            }

            TryRecordHistory(runtime, vr, now);
            TryUpdateRealtime(runtime, vr);
            TryCheckAlarm(runtime, vr);
        }

        /// <inheritdoc/>
        public void StopDevice(int deviceId)
        {
            if (!_queues.TryRemove(deviceId, out var queue))
            {
                return;
            }

            // 完成通道 → 消费任务排空后退出；超时 3s 记 Warning 放弃等待（防止通知链路挂死阻塞设备注销）。
            queue.Channel.Writer.TryComplete();
            var consumer = queue.ConsumerTask;
            if (consumer != null)
            {
                try
                {
                    consumer.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("设备 #{DeviceId} 通知泵停止超时（3s），已放弃等待。", deviceId);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("设备 #{DeviceId} 通知泵停止被取消，已放弃等待。", deviceId);
                }
                catch (Exception ex)
                {
                    // 消费任务异常已就地记录，此处忽略聚合异常。
                    _logger.LogDebug(ex, "设备 #{DeviceId} 通知泵消费任务退出时出现非预期异常（已忽略）。", deviceId);
                }
            }
        }

        // ===================== 通知泵（Step 3.2） =====================

        private void EnqueueNotification(DeviceRuntime runtime, VariableRuntime vr, object? value, VariableQuality quality, DateTime updateTime)
        {
            EnqueueNotification(runtime.Device.Id, runtime.Device.Key, vr.Key, value, quality, updateTime);
        }

        private void EnqueueNotification(int deviceId, string deviceKey, string variableKey, object? value, VariableQuality quality, DateTime updateTime)
        {
            var queue = _queues.GetOrAdd(deviceId, static _ => new DeviceNotificationQueue());

            // 消费任务仅启动一次（GetOrAdd 竞态下双重检查，锁内二次校验）。
            if (queue.ConsumerTask == null)
            {
                lock (queue.Sync)
                {
                    queue.ConsumerTask ??= Task.Run(() => ConsumeNotificationsAsync(queue, deviceKey));
                }
            }

            // DropOldest 语义：通道满时写成功但丢最旧项。写前按当前存量近似计数（仅日志观测，
            // 不做精确保证）；每 DropLogInterval 条记一次 Warning（含设备 Key 与累计丢弃数）。
            if (queue.Channel.Reader.CanCount && queue.Channel.Reader.Count >= NotificationCapacity)
            {
                var dropped = Interlocked.Increment(ref queue.DroppedCount);
                if (dropped % DropLogInterval == 0)
                {
                    _logger.LogWarning(
                        "设备 {DeviceKey}(#{DeviceId}) 通知通道溢出（容量 {Capacity}），累计丢弃 {Dropped} 条。",
                        deviceKey, deviceId, NotificationCapacity, dropped);
                }
            }

            queue.Channel.Writer.TryWrite(new VariableNotification(deviceId, deviceKey, variableKey, value, quality, updateTime));
        }

        /// <summary>
        /// 单设备通知消费循环：逐条推送，单条失败 try-catch 记 Warning 不中断泵；
        /// 通道完成且排空后任务退出。
        /// </summary>
        private async Task ConsumeNotificationsAsync(DeviceNotificationQueue queue, string deviceKey)
        {
            try
            {
                await foreach (var n in queue.Channel.Reader.ReadAllAsync())
                {
                    try
                    {
                        await _notificationService.NotifyVariableUpdateAsync(n.DeviceId, n.VariableKey, n.Value, n.Quality, n.UpdateTime);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "通知变量 {Key} 更新失败（设备 {DeviceKey}）。", n.VariableKey, n.DeviceKey);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备 {DeviceKey} 通知泵消费循环发生未预期异常。", deviceKey);
            }
        }

        // ===================== 历史 / 实时 / 报警（迁移自 DeviceWorker，逻辑等价） =====================

        /// <summary>
        /// 按变量存储策略决定是否记录历史采样点并异步入队（语义与迁移前 DeviceWorker.TryRecordHistory 一致）。
        /// </summary>
        private void TryRecordHistory(DeviceRuntime runtime, VariableRuntime vr, DateTime now)
        {
            try
            {
                if (vr.StoreMode == StoreModeEnum.None)
                {
                    return;
                }

                // 安全保护：周期异常（<1s）按每轮都写处理，避免配置损坏时静默停写。
                var intervalMs = vr.StoreIntervalMs > 0 ? vr.StoreIntervalMs : 1000;

                // 定时/兜底是否到期。vr.LastHistoryTime 初始为 MinValue，首次必写种子点。
                var due = (now - vr.LastHistoryTime).TotalMilliseconds >= intervalMs;

                var shouldWrite = due;

                // Change 模式额外支持"变化触发"：值有效变化且未到写库时也写。
                if (!shouldWrite && vr.StoreMode == StoreModeEnum.Change && vr.IsChanged)
                {
                    shouldWrite = IsEffectiveChange(vr);
                }

                if (!shouldWrite)
                {
                    return;
                }

                // 数值化：数字量（bool）→ 0/1；数值型 → double；其余 → 0（原始值保留在 RawValue）。
                var numericValue = ToNumeric(vr.Value);
                var rawValue = vr.Value?.ToString();

                _historyRecorder.Record(
                    runtime.Device.Id,
                    runtime.Device.Key,
                    vr.Key,
                    vr.Name,
                    numericValue,
                    rawValue,
                    vr.Quality.ToString(),
                    vr.UpdateTime);

                // 推进"最后写入"状态，供下次定时/兜底与死区判定使用。
                vr.LastHistoryTime = now;
                vr.LastHistoryWrittenValue = numericValue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "记录变量 {Key} 历史采样点失败（设备 {DeviceKey}）。", vr.Key, runtime.Device.Key);
            }
        }

        /// <summary>
        /// 判定"值变化"是否为一次有效的历史写入（Change 模式，死区抑制微小抖动）。
        /// </summary>
        private static bool IsEffectiveChange(VariableRuntime vr)
        {
            var deadBand = vr.DeadBand;
            if (deadBand is null || deadBand <= 0 || vr.LastHistoryWrittenValue is null || vr.Value is null)
            {
                return true;
            }

            var current = TryToNumber(vr.Value);
            if (current is null)
            {
                return true; // 非数值型变量不使用死区
            }

            return Math.Abs(current.Value - vr.LastHistoryWrittenValue.Value) > deadBand.Value;
        }

        /// <summary>
        /// 更新变量实时快照（MySQL 实时库）。每次成功处理都刷新，不依赖存储策略。
        /// </summary>
        private void TryUpdateRealtime(DeviceRuntime runtime, VariableRuntime vr)
        {
            try
            {
                if (vr.Value == null)
                {
                    return;
                }

                _realtimeSnapshot.Update(
                    runtime.Device.Id,
                    runtime.Device.Key,
                    vr.Key,
                    vr.Name,
                    ToNumeric(vr.Value),
                    vr.Value.ToString(),
                    vr.Quality.ToString(),
                    vr.UpdateTime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "更新变量 {Key} 实时快照失败（设备 {DeviceKey}）。", vr.Key, runtime.Device.Key);
            }
        }

        /// <summary>
        /// 检测变量报警（规则引擎权威 + Min/Max 兜底）。临界区持 <see cref="DeviceRuntime.AlarmSync"/>，
        /// 串行化字典读写与规则求值；临界区内仅内存操作（通知与落库均为 fire-and-forget）。
        /// </summary>
        private void TryCheckAlarm(DeviceRuntime runtime, VariableRuntime vr)
        {
            if (vr.Value == null)
            {
                return;
            }

            var rules = _alarmRuleEngine.GetRules(runtime.Device.Id, vr.Key);

            lock (runtime.AlarmSync)
            {
                // 规则热更新/删除后清理残留状态（语义同迁移前）。
                PruneStaleRuleStates(runtime, vr.Key, rules);

                if (rules.Count > 0)
                {
                    // 已配置规则：规则为权威，兜底不再参与，避免双报。
                    var numeric = TryToNumber(vr.Value);
                    if (numeric == null)
                    {
                        return; // 非数值型变量不参与数值规则
                    }

                    foreach (var rule in rules)
                    {
                        EvaluateRule(runtime, vr, rule, numeric.Value);
                    }
                    return;
                }

                // 未配置规则：Min/Max 上下限兜底。
                CheckMinMaxLimit(runtime, vr);
            }
        }

        /// <summary>逐条规则求值并驱动报警状态机（触发/恢复/防抖/去重）。</summary>
        private void EvaluateRule(DeviceRuntime runtime, VariableRuntime vr, AlarmRuleSnapshot rule, double value)
        {
            var matched = AlarmConditionEvaluator.IsMatched(rule.Condition, value, rule.Threshold);
            var key = BuildRuleStateKey(vr.Key, rule.Id);
            runtime.RuleStates.TryGetValue(key, out var state);
            state ??= new DeviceRuntime.AlarmRuleState { RuleId = rule.Id };

            if (matched)
            {
                if (state.IsActive)
                {
                    return; // 已报警且持续命中，不重复推送
                }

                if (rule.DebounceSeconds > 0)
                {
                    // 防抖观察：首次命中记录起点，持续命中超过防抖窗口才正式报警。
                    if (state.DebouncePending)
                    {
                        if ((DateTime.UtcNow - state.TriggerTime) >= TimeSpan.FromSeconds(rule.DebounceSeconds))
                        {
                            state.DebouncePending = false;
                            state.IsActive = true;
                            FireEvent(runtime, vr, rule, value, AlarmEventType.Triggered);
                        }
                    }
                    else
                    {
                        state.DebouncePending = true;
                        state.TriggerTime = DateTime.UtcNow;
                    }
                }
                else
                {
                    state.IsActive = true;
                    FireEvent(runtime, vr, rule, value, AlarmEventType.Triggered);
                }

                runtime.RuleStates[key] = state;
            }
            else
            {
                if (state.IsActive)
                {
                    // 退出报警态：推送恢复事件。
                    state.IsActive = false;
                    state.DebouncePending = false;
                    FireEvent(runtime, vr, rule, value, AlarmEventType.Recovered);
                    runtime.RuleStates[key] = state;
                }
                else if (state.DebouncePending)
                {
                    // 防抖观察期内恢复：视为抖动，取消本次报警。
                    state.DebouncePending = false;
                    runtime.RuleStates[key] = state;
                }
            }
        }

        /// <summary>构造规则报警状态键（保证同变量多规则状态互不干扰；兜底用 "$Key#"）。</summary>
        private static string BuildRuleStateKey(string variableKey, long ruleId) => variableKey + "#" + ruleId.ToString(CultureInfo.InvariantCulture);

        /// <summary>清理该变量下已不存在的规则残留状态（规则热更新后规则可能被删除/换绑）。</summary>
        private static void PruneStaleRuleStates(DeviceRuntime runtime, string variableKey, IReadOnlyList<AlarmRuleSnapshot> rules)
        {
            if (runtime.RuleStates.Count == 0)
            {
                return;
            }

            var prefix = variableKey + "#";
            List<string>? staleKeys = null;
            foreach (var pair in runtime.RuleStates)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = pair.Key.LastIndexOf('#');
                var idPart = separatorIndex >= 0 ? pair.Key[(separatorIndex + 1)..] : string.Empty;
                var isStale = !long.TryParse(idPart, out var ruleId)
                              || rules.All(r => r.Id != ruleId);
                if (isStale)
                {
                    (staleKeys ??= new List<string>()).Add(pair.Key);
                }
            }

            if (staleKeys != null)
            {
                foreach (var key in staleKeys)
                {
                    runtime.RuleStates.Remove(key);
                }
            }
        }

        /// <summary>将条件枚举映射为可读文本（用于默认报警文案）。</summary>
        private static string ConditionText(TriggerConditionEnum condition) => condition switch
        {
            TriggerConditionEnum.GreaterThan => "大于",
            TriggerConditionEnum.GreaterOrEqual => "大于等于",
            TriggerConditionEnum.LessThan => "小于",
            TriggerConditionEnum.LessOrEqual => "小于等于",
            TriggerConditionEnum.EqualTo => "等于",
            TriggerConditionEnum.NotEqualTo => "不等于",
            _ => condition.ToString()
        };

        /// <summary>Min/Max 数值上下限兜底报警（保持原去重语义，事件来源标记为 MinMaxLimit）。</summary>
        private void CheckMinMaxLimit(DeviceRuntime runtime, VariableRuntime vr)
        {
            var definition = vr.Definition;
            if (vr.Value == null || (definition.Min == null && definition.Max == null))
            {
                return;
            }

            var numeric = TryToNumber(vr.Value);
            if (numeric == null)
            {
                return; // 非数值型不参与上下限报警
            }

            var high = definition.Max != null && numeric.Value > definition.Max.Value;
            var low = definition.Min != null && numeric.Value < definition.Min.Value;

            runtime.AlarmStates.TryGetValue(vr.Key, out var last);

            if (high && !last.High)
            {
                runtime.AlarmStates[vr.Key] = (true, low);
                FireEvent(runtime, vr, null, numeric.Value, AlarmEventType.Triggered,
                    $"变量值 {numeric.Value} 超过上限 {definition.Max}", AlarmLevelEnum.High);
            }
            else if (low && !last.Low)
            {
                runtime.AlarmStates[vr.Key] = (high, true);
                FireEvent(runtime, vr, null, numeric.Value, AlarmEventType.Triggered,
                    $"变量值 {numeric.Value} 低于下限 {definition.Min}", AlarmLevelEnum.Medium);
            }
            else if (!high && !low && (last.High || last.Low))
            {
                // 恢复在限内：复位状态 + 推送恢复事件
                runtime.AlarmStates[vr.Key] = (false, false);
                FireEvent(runtime, vr, null, numeric.Value, AlarmEventType.Recovered, string.Empty, AlarmLevelEnum.High);
            }
        }

        /// <summary>
        /// 构造报警事件并推送（fire-and-forget 通知）+ 异步落库（AlarmRecorder）。
        /// 规则告警传 rule；兜底告警 rule 传 null 并用 levelOverride 指定级别。
        /// </summary>
        private void FireEvent(DeviceRuntime runtime, VariableRuntime vr, AlarmRuleSnapshot? rule, double value, AlarmEventType type, string? message = null, AlarmLevelEnum? levelOverride = null)
        {
            try
            {
                var level = levelOverride ?? (rule?.Level ?? AlarmLevelEnum.High);
                var actualValue = value.ToString(CultureInfo.InvariantCulture);

                var evt = new AlarmEvent
                {
                    EventType = type,
                    DeviceId = runtime.Device.Id,
                    DeviceKey = runtime.Device.Key,
                    VariableKey = vr.Key,
                    // 关联数据点模板（DataPoint.Id）；设备实例未映射到模板时回退模板主键兜底。
                    DataPointId = vr.Instance?.DataPointId ?? vr.Definition.Id,
                    VariableName = vr.Name,
                    RuleId = rule?.Id,
                    RuleName = rule?.Name,
                    Level = level,
                    Condition = rule?.Condition,
                    Threshold = rule?.Threshold,
                    ActualValue = actualValue,
                    Message = !string.IsNullOrEmpty(message)
                                ? message
                                : (rule != null
                                    ? $"{vr.Name} {ConditionText(rule.Condition)} {rule.Threshold.ToString(CultureInfo.InvariantCulture)}"
                                    : $"{vr.Name} 越界报警"),
                    Source = rule != null ? AlarmSourceEnum.Rule : AlarmSourceEnum.MinMaxLimit,
                    TriggeredAt = DateTime.UtcNow
                };

                // 设备级报警聚合打点：Triggered +1 / Recovered -1，clamp 下界 0（纯内存，无抛出路径）。
                runtime.ApplyAlarmDelta(type == AlarmEventType.Triggered ? 1 : -1);

                // 通知（SignalR）与落库均为异步安全路径：fire-and-forget 通知 + 非阻塞入队落库。
                _ = _notificationService.NotifyAlarmAsync(evt);
                _alarmRecorder.Record(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "推送/记录报警失败: {VariableKey}", vr.Key);
            }
        }

        // ===================== 工具方法（迁移自 DeviceWorker） =====================

        /// <summary>尝试将变量值转为数值；非数值（布尔/字符串等按 0/1 或失败跳过）。</summary>
        private static double? TryToNumber(object value)
        {
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                // 布尔/字符串：统一按 0/1 参与（约定 true=1, false=0），无法转换则跳过。
                if (value is bool b) return b ? 1.0 : 0.0;
                return null;
            }
        }

        /// <summary>数值化变量值：数字量（bool）→ 0/1；数值型 → double；其余 → 0。</summary>
        private static double ToNumeric(object? value)
        {
            if (value is bool flag)
            {
                return flag ? 1 : 0;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 值相等比较，优先引用/类型相等，其次按数值相等（覆盖 int/long/double 同值但类型不同的场景），用于回声抑制判定。
        /// </summary>
        private static bool ValueEquals(object? a, object? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Equals(b)) return true;
            try
            {
                return Convert.ToDouble(a, CultureInfo.InvariantCulture) == Convert.ToDouble(b, CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>单条通知载荷（对齐 <see cref="IScadaNotificationService.NotifyVariableUpdateAsync"/> 签名）。</summary>
        private readonly record struct VariableNotification(
            int DeviceId,
            string DeviceKey,
            string VariableKey,
            object? Value,
            VariableQuality Quality,
            DateTime UpdateTime);

        /// <summary>每设备通知队列：有界通道 + 单消费任务 + 丢弃计数（供溢出日志观测）。</summary>
        private sealed class DeviceNotificationQueue
        {
            /// <summary>消费任务启动双重检查锁（仅保护 ConsumerTask 首次赋值）。</summary>
            public object Sync { get; } = new();

            public Channel<VariableNotification> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<VariableNotification>(
                new BoundedChannelOptions(NotificationCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });

            /// <summary>消费任务句柄：StopDevice 时等待其退出。</summary>
            public Task? ConsumerTask { get; set; }

            /// <summary>累计丢弃条数（Interlocked 计数，仅日志观测）。</summary>
            public long DroppedCount;
        }
    }
}
