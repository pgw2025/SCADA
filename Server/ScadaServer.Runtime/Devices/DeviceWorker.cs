using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Alarms;
using ScadaServer.Runtime.Events;

namespace ScadaServer.Runtime.Devices
{
    /// <summary>
    /// 设备工作器，负责单台设备的数据采集和驱动通讯。
    /// 以变量级轮询周期（RuntimeVariable.PollingIntervalMs）驱动采集，支持变量变化检测、质量状态管理与平均响应时间计算。
    /// </summary>
    /// <remarks>
    /// 每个设备运行时对应一个 DeviceWorker 实例，由 DeviceScheduler 调度执行。
    /// 采集节奏由每个 RuntimeVariable 的 NextPollTime 决定（各自 PollingIntervalMs），
    /// 不再依赖单一设备级固定延迟。地址等实现细节统一由 RuntimeVariable 解析（来自 DeviceVariable）。
    /// </remarks>
    public class DeviceWorker
    {
        private readonly DeviceRuntime _runtime;
        private readonly ILogger<DeviceWorker> _logger;
        private readonly IScadaNotificationService _notificationService;
        private readonly IHistoryRecorder _historyRecorder;
        private readonly IVariableChangeBus _changeBus;
        private readonly IAlarmRuleEngine _alarmRuleEngine;
        private readonly IAlarmRecorder _alarmRecorder;

        /// <summary>
        /// 变量越界报警去重状态：key = 变量Key，值 = (是否超上限, 是否低于限)。
        /// 仅在进入越界时推送一次，恢复在限内后复位，避免持续越界刷屏报警。
        /// </summary>
        private readonly Dictionary<string, (bool High, bool Low)> _alarmStates = new();

        /// <summary>
        /// 规则报警去重/防抖状态：key = "$variableKey#$ruleId"（或 "$variableKey#" 表示兜底）。
        /// </summary>
        private readonly Dictionary<string, AlarmRuleState> _ruleStates = new();

        /// <summary>
        /// 初始化设备工作器
        /// </summary>
        /// <param name="runtime">设备运行时，包含设备配置、驱动实例和变量集合</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="notificationService">变量更新通知服务（SignalR / MQTT）</param>
        /// <param name="historyRecorder">历史数据记录器（异步落库）</param>
        /// <param name="changeBus">变量变化事件总线</param>
        /// <param name="alarmRuleEngine">报警规则引擎（规则命中判定）</param>
        /// <param name="alarmRecorder">报警记录器（异步落库）</param>
        /// <exception cref="ArgumentNullException">runtime 或 logger 为 null 时抛出</exception>
        public DeviceWorker(DeviceRuntime runtime, ILogger<DeviceWorker> logger, IScadaNotificationService notificationService, IHistoryRecorder historyRecorder, IVariableChangeBus changeBus, IAlarmRuleEngine alarmRuleEngine, IAlarmRecorder alarmRecorder)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _historyRecorder = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
            _changeBus = changeBus ?? throw new ArgumentNullException(nameof(changeBus));
            _alarmRuleEngine = alarmRuleEngine ?? throw new ArgumentNullException(nameof(alarmRuleEngine));
            _alarmRecorder = alarmRecorder ?? throw new ArgumentNullException(nameof(alarmRecorder));
        }

        /// <summary>
        /// 启动设备采集循环
        /// </summary>
        /// <param name="cancellationToken">取消令牌，用于停止采集循环</param>
        /// <returns>任务完成时返回</returns>
        public async Task WorkerAsync(CancellationToken cancellationToken)
        {
            // 检查驱动是否已分配，无驱动则无法工作
            if (_runtime.Driver == null)
            {
                _logger.LogWarning("Device {DeviceKey} has no driver assigned.", _runtime.Device.Key);
                return;
            }

            // 驱动连接成功状态已由设备注册阶段置为 Connected，此处不再强置 Initializing，
            // 避免覆盖启动即在线状态（尤其空转/无启变量设备）。
            _logger.LogInformation("DeviceWorker {DeviceKey} initializing...", _runtime.Device.Key);

            // 主采集循环，直到收到取消信号
            while (!cancellationToken.IsCancellationRequested)
            {
                // 计时器用于统计本轮采集耗时
                var sw = Stopwatch.StartNew();
                var now = DateTime.Now;
                try
                {
                    var changed = new List<(string Key, object Value)>();

                    // 收集本轮到期的变量（按各自 PollingIntervalMs 调度）
                    var due = new List<VariableRuntime>();
                    foreach (var vr in _runtime.Variables.Values)
                    {
                        if (!vr.IsEnabled) continue;
                        if (now >= vr.NextPollTime) due.Add(vr);
                    }

                    if (due.Count == 0)
                    {
                        // 本 tick 无到期变量（含无启用变量的空转设备）：驱动连接依然有效，
                        // 保持 Connected，避免始终停留在 Initializing/Offline（空转设备离线的根因之一）。
                        _runtime.ConnectionState = DeviceConnectionState.Connected;

                        // 无到期变量：休眠至最近一次下次轮询时间，兼顾调度精度与退出响应性
                        var soonest = DateTime.MaxValue;
                        foreach (var vr in _runtime.Variables.Values)
                        {
                            if (vr.IsEnabled && vr.NextPollTime < soonest) soonest = vr.NextPollTime;
                        }

                        var waitMs = soonest == DateTime.MaxValue
                            ? _runtime.Device.PollingInterval
                            : (int)Math.Max(0, (soonest - now).TotalMilliseconds);
                        // 上限 2000ms：避免长时间阻塞导致配置变更 / 取消信号响应不及时
                        waitMs = Math.Min(waitMs, 2000);

                        if (waitMs > 0)
                        {
                            try { await Task.Delay(waitMs, cancellationToken); }
                            catch (OperationCanceledException) { break; }
                        }
                        continue;
                    }

                    // 逐个读取到期变量。
                    // 第九阶段起：驱动只接收 RuntimeVariable（IRuntimeVariable 视图），
                    // 地址 / 位偏移 / 轮询 / 缩放等由 RuntimeVariable 解析（来自 DeviceVariable），
                    // 驱动不再感知 ModelVariable 模板实体。
                    foreach (var vr in due)
                    {
                        try
                        {
                            var newValue = await _runtime.Driver.ReadAsync(vr);

                            // 驱动可能返回 null（例如虚拟设备未连接、订阅型驱动暂无数据）。
                            // 视为本次读取无效：跳过值更新,避免 null 被当作变化值推送到前端。
                            if (newValue == null)
                            {
                                vr.Quality = VariableQuality.CommunicationError;
                                continue;
                            }

                            // 在设备级锁内更新内存态，与 WriteVariableAsync（绑定写入/用户写入）临界区串行化，消除并发读写竞态。
                            await _runtime.Lock.WaitAsync();
                            try
                            {
                                vr.PreviousValue = vr.Value;
                                vr.Value = newValue;
                                vr.UpdateTime = now;
                                vr.Quality = VariableQuality.Good;

                                // 回声抑制：若本次回读值等于绑定引擎最近一次写入的期望值且落在窗口内，
                                // 视为绑定写入的回显，不发布变化事件。
                                var echoWindowMs = Math.Max(vr.PollingIntervalMs, 1000) * 2;
                                var isEcho = vr.LastBindingWriteValue != null
                                             && (now - vr.LastBindingWriteTime).TotalMilliseconds <= echoWindowMs
                                             && ValueEquals(newValue, vr.LastBindingWriteValue);

                                vr.IsChanged = !Equals(vr.Value, vr.PreviousValue) && !isEcho;
                            }
                            finally
                            {
                                _runtime.Lock.Release();
                            }

                            if (vr.IsChanged && vr.Value != null)
                            {
                                changed.Add((vr.Key, vr.Value));

                                // 发布进程内变量变化事件（非阻塞），供绑定引擎等订阅者消费。
                                _changeBus.Publish(new VariableChangeEvent
                                {
                                    DeviceId = _runtime.Device.Id,
                                    VariableKey = vr.Key,
                                    Value = vr.Value,
                                    PreviousValue = vr.PreviousValue,
                                    Quality = vr.Quality,
                                    UpdateTime = vr.UpdateTime,
                                    Source = VariableChangeSource.Polling
                                });
                            }

                            // 按变量存储策略记录历史采样点（异步入队，不阻塞采集）
                            TryRecordHistory(vr);

                            // 检测变量上下限越界并推送系统报警（仅进入越界时推送一次）
                            TryCheckAlarm(vr);
                        }
                        catch (Exception ex)
                        {
                            // 单个变量读取失败，标记通信错误但不中断其他变量
                            vr.Quality = VariableQuality.CommunicationError;
                            _logger.LogError(ex, "Read variable {VariableName} failed.", vr.Name);
                        }
                        finally
                        {
                            // 无论成功或失败，均推进该变量下一次轮询时间
                            vr.NextPollTime = now.AddMilliseconds(vr.PollingIntervalMs);
                        }
                    }

                    // 将发生变化的变量推送到 SignalR / MQTT
                    foreach (var (key, value) in changed)
                    {
                        try
                        {
                            await _notificationService.NotifyVariableUpdateAsync(_runtime.Device.Id, key, value);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "通知变量 {Key} 更新失败。", key);
                        }
                    }

                    // 本轮采集成功，更新设备状态
                    _runtime.ConnectionState = DeviceConnectionState.Connected;
                    _runtime.LastCommunicationTime = DateTime.Now;
                    _runtime.SuccessCount++;
                    _runtime.ConsecutiveFailureCount = 0;
                }
                catch (Exception ex)
                {
                    // 本轮采集整体失败，更新设备错误状态
                    _runtime.ConnectionState = DeviceConnectionState.Error;
                    _runtime.FailureCount++;
                    _runtime.ConsecutiveFailureCount++;
                    _logger.LogError(ex, "DeviceWorker {DeviceKey} encountered an error.", _runtime.Device.Key);
                }
                finally
                {
                    // 更新平均响应时间（基于成功次数的移动平均；尚未成功时直接取本轮耗时）
                    sw.Stop();
                    if (_runtime.SuccessCount > 0)
                    {
                        _runtime.AverageResponseTime =
                            (_runtime.AverageResponseTime * (_runtime.SuccessCount - 1) + sw.Elapsed.TotalMilliseconds)
                            / _runtime.SuccessCount;
                    }
                    else
                    {
                        _runtime.AverageResponseTime = sw.Elapsed.TotalMilliseconds;
                    }
                }

                // 节奏完全由变量级 NextPollTime 控制，此处不再使用设备级固定延迟。
            }

            // 循环结束，标记设备断开
            _runtime.ConnectionState = DeviceConnectionState.Disconnected;
            _logger.LogInformation("DeviceWorker {DeviceKey} stopped.", _runtime.Device.Key);
        }

        /// <summary>
        /// 按变量存储策略决定是否记录历史采样点并异步入队。
        /// <list type="bullet">
        /// <item>None：不存储；</item>
        /// <item>Change：值变化时记录；</item>
        /// <item>Cycle / Compressed / Aggregated：本阶段统一按采集周期记录原始点。</item>
        /// </list>
        /// </summary>
        private void TryRecordHistory(VariableRuntime vr)
        {
            var storeMode = vr.Definition.StoreMode;
            if (storeMode == StoreModeEnum.None)
            {
                return;
            }

            // Change 模式仅在值变化时记录；周期类模式每轮采集都记录。
            if (storeMode == StoreModeEnum.Change && !vr.IsChanged)
            {
                return;
            }

            // 数值化：数字量（bool）→ 0/1；数值型 → double；其余 → 0（原始值保留在 RawValue）。
            double numericValue = 0;
            string? rawValue = vr.Value?.ToString();
            if (vr.Value != null)
            {
                if (vr.Value is bool flag)
                {
                    numericValue = flag ? 1 : 0;
                }
                else
                {
                    try
                    {
                        numericValue = Convert.ToDouble(vr.Value);
                    }
                    catch
                    {
                        numericValue = 0;
                    }
                }
            }

            _historyRecorder.Record(
                _runtime.Device.Id,
                _runtime.Device.Key,
                vr.Key,
                vr.Name,
                numericValue,
                rawValue,
                vr.Quality.ToString());
        }

        /// <summary>
        /// 检测变量报警（SignalR ReceiveAlarm + 异步落库 AlarmRecorder）。
        /// <list type="bullet">
        /// <item>该设备+变量配置了活跃报警规则时，由规则引擎求值（支持防抖与去重），规则为权威；</item>
        /// <item>未配置规则时，回退到模型变量的数值上下限（Min / Max）兜底报警，来源标记为 MinMaxLimit；</item>
        /// <item>进入报警态推送一次触发事件，恢复后推送恢复事件，均经 AlarmRecorder 异步落库。</item>
        /// </list>
        /// </summary>
        private void TryCheckAlarm(VariableRuntime vr)
        {
            if (vr.Value == null)
            {
                return;
            }

            var rules = _alarmRuleEngine.GetRules(_runtime.Device.Id, vr.Key);
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
                    EvaluateRule(vr, rule, numeric.Value);
                }
                return;
            }

            // 未配置规则：Min/Max 上下限兜底。
            CheckMinMaxLimit(vr);
        }

        /// <summary>
        /// 逐条规则求值并驱动报警状态机（触发/恢复/防抖/去重）。
        /// </summary>
        private void EvaluateRule(VariableRuntime vr, AlarmRuleSnapshot rule, double value)
        {
            var matched = MatchesCondition(rule.Condition, value, rule.Threshold);
            var key = BuildRuleStateKey(vr.Key, rule.Id);
            _ruleStates.TryGetValue(key, out var state);
            state ??= new AlarmRuleState { RuleId = rule.Id };

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
                        if ((DateTime.Now - state.TriggerTime) >= TimeSpan.FromSeconds(rule.DebounceSeconds))
                        {
                            state.DebouncePending = false;
                            state.IsActive = true;
                            FireEvent(vr, rule, value, AlarmEventType.Triggered);
                        }
                    }
                    else
                    {
                        state.DebouncePending = true;
                        state.TriggerTime = DateTime.Now;
                    }
                }
                else
                {
                    state.IsActive = true;
                    FireEvent(vr, rule, value, AlarmEventType.Triggered);
                }

                _ruleStates[key] = state;
            }
            else
            {
                if (state.IsActive)
                {
                    // 退出报警态：推送恢复事件。
                    state.IsActive = false;
                    state.DebouncePending = false;
                    FireEvent(vr, rule, value, AlarmEventType.Recovered);
                    _ruleStates[key] = state;
                }
                else if (state.DebouncePending)
                {
                    // 防抖观察期内恢复：视为抖动，取消本次报警。
                    state.DebouncePending = false;
                    _ruleStates[key] = state;
                }
            }
        }

        /// <summary>
        /// 条件比较，浮点相等使用容差避免精度抖动。
        /// </summary>
        private static bool MatchesCondition(TriggerConditionEnum condition, double value, double threshold)
        {
            const double epsilon = 1e-9;
            return condition switch
            {
                TriggerConditionEnum.GreaterThan => value > threshold,
                TriggerConditionEnum.GreaterOrEqual => value >= threshold,
                TriggerConditionEnum.LessThan => value < threshold,
                TriggerConditionEnum.LessOrEqual => value <= threshold,
                TriggerConditionEnum.EqualTo => Math.Abs(value - threshold) <= epsilon,
                TriggerConditionEnum.NotEqualTo => Math.Abs(value - threshold) > epsilon,
                _ => false
            };
        }

        /// <summary>
        /// 构造规则报警状态键（保证同变量多规则状态互不干扰；兜底用 "$Key#"）。
        /// </summary>
        private static string BuildRuleStateKey(string variableKey, long ruleId) => variableKey + "#" + ruleId.ToString();

        /// <summary>
        /// 将条件枚举映射为可读文本（用于默认报警文案）。
        /// </summary>
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

        /// <summary>
        /// Min/Max 数值上下限兜底报警（保持原去重语义，事件来源标记为 MinMaxLimit）。
        /// </summary>
        private void CheckMinMaxLimit(VariableRuntime vr)
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

            if (!_alarmStates.TryGetValue(vr.Key, out var last))
            {
                last = (false, false);
            }

            if (high && !last.High)
            {
                _alarmStates[vr.Key] = (true, low);
                FireEvent(vr, null, numeric.Value, AlarmEventType.Triggered,
                    $"变量值 {numeric.Value} 超过上限 {definition.Max}", AlarmLevelEnum.High);
            }
            else if (low && !last.Low)
            {
                _alarmStates[vr.Key] = (high, true);
                FireEvent(vr, null, numeric.Value, AlarmEventType.Triggered,
                    $"变量值 {numeric.Value} 低于下限 {definition.Min}", AlarmLevelEnum.Medium);
            }
            else if (!high && !low && (last.High || last.Low))
            {
                // 恢复在限内：复位状态 + 推送恢复事件
                _alarmStates[vr.Key] = (false, false);
                FireEvent(vr, null, numeric.Value, AlarmEventType.Recovered, string.Empty, AlarmLevelEnum.High);
            }
        }

        /// <summary>
        /// 构造报警事件并推送（fire-and-forget 通知）+ 异步落库（AlarmRecorder）。
        /// 规则告警传 rule；兜底告警 rule 传 null 并用 levelOverride 指定级别。
        /// </summary>
        private void FireEvent(VariableRuntime vr, AlarmRuleSnapshot? rule, double value, AlarmEventType type, string? message = null, AlarmLevelEnum? levelOverride = null)
        {
            try
            {
                var level = levelOverride ?? (rule?.Level ?? AlarmLevelEnum.High);
                var actualValue = value.ToString(CultureInfo.InvariantCulture);

                var evt = new AlarmEvent
                {
                    EventType = type,
                    DeviceId = _runtime.Device.Id,
                    DeviceKey = _runtime.Device.Key,
                    VariableKey = vr.Key,
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
                    TriggeredAt = DateTime.Now
                };

                // 通知（SignalR）与落库均为异步安全路径：fire-and-forget 通知 + 非阻塞入队落库。
                _ = _notificationService.NotifyAlarmAsync(evt);
                _alarmRecorder.Record(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "推送/记录报警失败: {VariableKey}", vr.Key);
            }
        }

        /// <summary>
        /// 尝试将变量值转为数值；非数值（布尔/字符串等按 0/1 或失败跳过）。
        /// </summary>
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
                return Convert.ToDouble(a) == Convert.ToDouble(b);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 单条规则的报警状态机（内存态，不持久化）。
        /// </summary>
        private class AlarmRuleState
        {
            public long RuleId { get; init; }
            public bool IsActive { get; set; }
            public bool DebouncePending { get; set; }
            public DateTime TriggerTime { get; set; }
        }
    }
}
