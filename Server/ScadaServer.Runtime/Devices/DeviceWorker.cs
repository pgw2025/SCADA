using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Alarms;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Alarms;
using ScadaServer.Runtime.DataConversion;
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
    /// 不再依赖单一设备级固定延迟。地址等实现细节统一由 RuntimeVariable 解析（来自 DataPointMapping）。
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
        private readonly IRealtimeSnapshotService _realtimeSnapshot;

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
        /// 断线判定阈值：连续 N 个采集轮次全部失败即判定设备断线，转入自动重连流程。
        /// N=3：按默认 1s 轮询约 3s 检测延迟，兼顾对短暂网络抖动的容忍与故障发现速度。
        /// </summary>
        private const int ReconnectAfterConsecutiveFailures = 3;

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
        /// <param name="realtimeSnapshot">实时快照服务（MySQL 实时库）</param>
        /// <exception cref="ArgumentNullException">runtime 或 logger 为 null 时抛出</exception>
        public DeviceWorker(DeviceRuntime runtime, ILogger<DeviceWorker> logger, IScadaNotificationService notificationService, IHistoryRecorder historyRecorder, IVariableChangeBus changeBus, IAlarmRuleEngine alarmRuleEngine, IAlarmRecorder alarmRecorder, IRealtimeSnapshotService realtimeSnapshot)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _historyRecorder = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
            _changeBus = changeBus ?? throw new ArgumentNullException(nameof(changeBus));
            _alarmRuleEngine = alarmRuleEngine ?? throw new ArgumentNullException(nameof(alarmRuleEngine));
            _alarmRecorder = alarmRecorder ?? throw new ArgumentNullException(nameof(alarmRecorder));
            _realtimeSnapshot = realtimeSnapshot ?? throw new ArgumentNullException(nameof(realtimeSnapshot));
        }

        /// <summary>
        /// 启动设备采集循环
        /// </summary>
        /// <param name="cancellationToken">取消令牌，用于停止采集循环</param>
        /// <returns>任务完成时返回</returns>
        public async Task WorkerAsync(CancellationToken cancellationToken)
        {
            // 待重连占位运行时：不采集，由调度器触发重连后重建 Worker。
            if (_runtime.NeedsReconnect)
            {
                return;
            }

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
                // 项目时间戳约定：运行时统一使用 UTC（历史库 InfluxStore 会再做 ToUniversalTime，
                // Kind=Utc 时为 no-op；跨时区部署 / 系统时区变更时不会产生偏移）。
                var now = DateTime.UtcNow;

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

                // 计时器仅覆盖实际采集段：空转等待发生在计时范围之外，
                // 避免空闲休眠被计入平均响应时间导致统计值持续膨胀。
                var sw = Stopwatch.StartNew();
                try
                {
                    // 待推送通知（值变化或质量跃迁）：携带质量与采集时间，由后台任务非阻塞推送。
                    var notifications = new List<(string Key, object? Value, VariableQuality Quality, DateTime UpdateTime)>();

                    // 轮次级成功统计：本轮至少一个变量读取成功才视为通讯成功，
                    // 用于收尾时区分"部分成功=在线"与"全部失败=通讯故障"。
                    var anySuccess = false;

                    // 逐个读取到期变量。
                    // 第九阶段起：驱动只接收 RuntimeVariable（IRuntimeVariable 视图），
                    // 地址 / 位偏移 / 轮询 / 缩放等由 RuntimeVariable 解析（来自 DataPointMapping），
                    // 驱动不再感知 DataPoint 模板实体。
                    foreach (var vr in due)
                    {
                        var previousQuality = vr.Quality;
                        try
                        {
                            var newValue = await _runtime.Driver.ReadAsync(vr);

                            // 工程换算（raw → engineering）：表达式为空即恒等，求值失败保持原始值，
                            // 保证一条坏配置最多让该变量按原始值上报，不拖垮采集循环。
                            // 换算后的值统一进入历史存储、死区判定、报警判定与 SignalR 推送（均为工程单位语义）。
                            newValue = VariableScaling.ToEngineering(vr, newValue);

                            // 驱动可能返回 null（例如虚拟设备未连接、订阅型驱动暂无数据）。
                            // 视为本次读取无效：跳过值更新,避免 null 被当作变化值推送到前端。
                            if (newValue == null)
                            {
                                vr.Quality = VariableQuality.CommunicationError;
                                // 质量降级（好→坏）推送一次：值保持最近一次有效值（僵尸值），
                                // 前端据此在监控页标记"通讯异常"，而非无感知地继续展示旧值。
                                if (previousQuality != VariableQuality.CommunicationError)
                                {
                                    notifications.Add((vr.Key, vr.Value, vr.Quality, now));
                                }
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

                            // 值变化或质量跃迁（坏→好恢复）都推送：质量恢复时即使值未变，
                            // 前端也需要清除"通讯异常"标记。
                            if (vr.IsChanged || previousQuality != VariableQuality.Good)
                            {
                                notifications.Add((vr.Key, vr.Value, vr.Quality, vr.UpdateTime));
                            }

                            // 按变量存储策略记录历史采样点（异步入队，不阻塞采集）
                            TryRecordHistory(vr, now);

                            // 更新实时快照（内存态，后台批量 Upsert 到 MySQL 实时库），不阻塞采集
                            TryUpdateRealtime(vr);

                            // 检测变量上下限越界并推送系统报警（仅进入越界时推送一次）
                            TryCheckAlarm(vr);

                            // 读取并更新成功，计入轮次级成功统计。
                            anySuccess = true;
                        }
                        catch (Exception ex)
                        {
                            // 单个变量读取失败，标记通信错误但不中断其他变量
                            vr.Quality = VariableQuality.CommunicationError;
                            _runtime.LastError = TruncateError(ex.Message);
                            if (previousQuality != VariableQuality.CommunicationError)
                            {
                                notifications.Add((vr.Key, vr.Value, vr.Quality, now));
                            }
                            _logger.LogError(ex, "Read variable {VariableName} failed.", vr.Name);
                        }
                        finally
                        {
                            // 无论成功或失败，均推进该变量下一次轮询时间
                            vr.NextPollTime = now.AddMilliseconds(vr.PollingIntervalMs);
                        }
                    }

                    // 推送本轮通知（SignalR / MQTT）到后台任务执行，不阻塞采集节奏：
                    // 通知链路含 MQTT 发布（网络 IO 可能秒级），逐条 await 会挤占采集调度导致
                    // NextPollTime 漂移。后台任务内保持顺序推送并逐条兜底异常。
                    if (notifications.Count > 0)
                    {
                        _ = PushNotificationsAsync(_runtime.Device.Id, notifications);
                    }

                    // 本轮采集结果（轮次级判定，due.Count > 0 时才会到达此处）：
                    // 任一变量读取成功即视为通讯成功（部分成功 = 在线）；
                    // 全部变量读取失败判定为通讯故障，设备转 Error/Fault，
                    // 连续失败计数递增供外部监控告警使用。
                    if (anySuccess)
                    {
                        _runtime.ConnectionState = DeviceConnectionState.Connected;
                        _runtime.LastCommunicationTime = DateTime.UtcNow;
                        _runtime.SuccessCount++;
                        _runtime.ConsecutiveFailureCount = 0;
                    }
                    else
                    {
                        _runtime.ConnectionState = DeviceConnectionState.Error;
                        _runtime.FailureCount++;
                        _runtime.ConsecutiveFailureCount++;

                        // 全失败无异常路径：无可记异常对象，写聚合失败描述（与下方 LogWarning 文案对齐）
                        _runtime.LastError = TruncateError($"本轮 {due.Count} 个到期变量全部读取失败");

                        // 仅在首次失败时告警一次，持续失败由设备状态（Fault）体现，避免每轮刷屏。
                        if (_runtime.ConsecutiveFailureCount == 1)
                        {
                            _logger.LogWarning(
                                "Device {DeviceKey} 本轮 {Count} 个到期变量全部读取失败，设备转为 Error。",
                                _runtime.Device.Key, due.Count);
                        }

                        // 断线判定：连续多轮全部失败（容忍短暂网络抖动）即判定设备断线，
                        // 置位 NeedsReconnect 并退出采集循环，转入运行时自动重连流程——
                        // 调度器发现标记后按退避窗口触发 ReconnectDeviceAsync，重建驱动
                        // 连接与采集 Worker（复用初始连接失败的占位重连机制），
                        // 无需人工重启服务。任一轮成功会在上方清零计数自动解除判定。
                        if (_runtime.ConsecutiveFailureCount >= ReconnectAfterConsecutiveFailures)
                        {
                            _logger.LogWarning(
                                "设备 {DeviceKey} 连续 {Count} 轮采集全部失败，判定断线，转入自动重连流程。",
                                _runtime.Device.Key, _runtime.ConsecutiveFailureCount);
                            _runtime.NeedsReconnect = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 本轮采集整体失败，更新设备错误状态
                    _runtime.ConnectionState = DeviceConnectionState.Error;
                    _runtime.FailureCount++;
                    _runtime.ConsecutiveFailureCount++;
                    _runtime.LastError = TruncateError(ex.Message);
                    _logger.LogError(ex, "DeviceWorker {DeviceKey} encountered an error.", _runtime.Device.Key);
                }
                finally
                {
                    // 更新平均响应时间：基于总轮次（成功 + 失败）的累积移动平均。
                    // 修复旧公式缺陷——失败轮次走 SuccessCount>0 分支时 N 不增长，失败样本以
                    // "替换"而非"累积"方式混入，数学上不成立且数值系统性漂移。
                    // 失败轮次耗时同样计入，反映真实通讯耗时，便于监控劣化趋势。
                    sw.Stop();
                    _runtime.PollRoundCount++;
                    _runtime.AverageResponseTime +=
                        (sw.Elapsed.TotalMilliseconds - _runtime.AverageResponseTime) / _runtime.PollRoundCount;
                }

                // 节奏完全由变量级 NextPollTime 控制，此处不再使用设备级固定延迟。
            }

            // 循环结束，标记设备断开
            _runtime.ConnectionState = DeviceConnectionState.Disconnected;
            _logger.LogInformation("DeviceWorker {DeviceKey} stopped.", _runtime.Device.Key);
        }

        /// <summary>
        /// 后台推送本轮变量通知（SignalR 分组 + MQTT），由采集循环 fire-and-forget 调用：
        /// 保持轮内顺序、逐条兜底异常，任何单条失败不影响后续推送，也不阻塞采集节奏。
        /// </summary>
        private async Task PushNotificationsAsync(
            int deviceId,
            List<(string Key, object? Value, VariableQuality Quality, DateTime UpdateTime)> notifications)
        {
            foreach (var n in notifications)
            {
                try
                {
                    await _notificationService.NotifyVariableUpdateAsync(deviceId, n.Key, n.Value, n.Quality, n.UpdateTime);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "通知变量 {Key} 更新失败。", n.Key);
                }
            }
        }

        /// <summary>
        /// 按变量存储策略决定是否记录历史采样点并异步入队。
        /// <list type="bullet">
        /// <item>None：不存储；</item>
        /// <item>
        /// Change（变化存储）：值发生"有效变化"即写入（死区 <c>DeadBand</c> 抑制微小抖动）；
        /// 同时具备"超时兜底"——若值长时间未变化，超过 <c>StoreIntervalMs</c> 也强制写入一条，
        /// 避免趋势曲线断档。两者取"先到先写"。
        /// </item>
        /// <item>
        /// Cycle / Compressed / Aggregated（周期类存储）：按 <c>StoreIntervalMs</c> 定时写入原始点，
        /// 与轮询间隔解耦。
        /// </item>
        /// </list>
        /// <para>
        /// 判定基于运行时字段 <see cref="VariableRuntime.LastHistoryTime"/>：首次采集（MinValue）
        /// 必写一条"种子点"，并用于判定后续是否到期。采样时间戳取变量采集时刻 <c>vr.UpdateTime</c>，
        /// 而非入队/落库时间，避免队列积压导致时间戳整体偏移。
        /// </para>
        /// </summary>
        private void TryRecordHistory(VariableRuntime vr, DateTime now)
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
                vr.Quality.ToString(),
                vr.UpdateTime);

            // 推进"最后写入"状态，供下次定时/兜底与死区判定使用。
            vr.LastHistoryTime = now;
            vr.LastHistoryWrittenValue = numericValue;
        }

        /// <summary>
        /// 判定"值变化"是否为一次有效的历史写入（Change 模式）。
        /// <para>
        /// 使用死区 <c>DeadBand</c> 抑制微小抖动：数值型变量且配置了死区时，
        /// 仅当 |当前值 - 最近一次写入值| 大于死区才视为有效变化；未配死区或非数值型变量，
        /// 直接按变量变化标志（IsChanged）判定。全局 <see cref="VariableRuntime.IsChanged"/>
        /// 仍用于驱动 SignalR/MQTT/绑定事件，此处只影响历史写入，互不影响。
        /// </para>
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
        /// 更新变量实时快照（MySQL 实时库）。每次成功读取都刷新，
        /// 不依赖存储策略，保证实时表始终反映最新采集值/质量/时间。
        /// </summary>
        private void TryUpdateRealtime(VariableRuntime vr)
        {
            if (vr.Value == null)
            {
                return;
            }

            double numericValue = 0;
            string? rawValue = vr.Value?.ToString();
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

            _realtimeSnapshot.Update(
                _runtime.Device.Id,
                _runtime.Device.Key,
                vr.Key,
                vr.Name,
                numericValue,
                rawValue,
                vr.Quality.ToString(),
                vr.UpdateTime);
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

            // 规则热更新/删除后清理残留状态：规则已移除的状态若不清，重新上架同 ID 规则时
            // 旧状态 IsActive=true 会导致该规则永远不再触发（只有设备整体重载才重置）。
            PruneStaleRuleStates(vr.Key, rules);

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
            var matched = AlarmConditionEvaluator.IsMatched(rule.Condition, value, rule.Threshold);
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
                        if ((DateTime.UtcNow - state.TriggerTime) >= TimeSpan.FromSeconds(rule.DebounceSeconds))
                        {
                            state.DebouncePending = false;
                            state.IsActive = true;
                            FireEvent(vr, rule, value, AlarmEventType.Triggered);
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
        /// 构造规则报警状态键（保证同变量多规则状态互不干扰；兜底用 "$Key#"）。
        /// </summary>
        private static string BuildRuleStateKey(string variableKey, long ruleId) => variableKey + "#" + ruleId.ToString();

        /// <summary>
        /// 清理该变量下已不存在的规则残留状态（规则热更新周期 Reload 后规则可能被删除/换绑）。
        /// 状态键格式为 <c>variableKey#ruleId</c>，按前缀匹配并校验 ruleId 是否仍在活跃规则集合内。
        /// </summary>
        private void PruneStaleRuleStates(string variableKey, IReadOnlyList<AlarmRuleSnapshot> rules)
        {
            if (_ruleStates.Count == 0)
            {
                return;
            }

            var prefix = variableKey + "#";
            List<string>? staleKeys = null;
            foreach (var pair in _ruleStates)
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
                    _ruleStates.Remove(key);
                }
            }
        }

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

        /// <summary>失败原因截断（上限 500 字符），防止异常消息超长撑爆快照/日志。</summary>
        private static string? TruncateError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;
            return message.Length <= 500 ? message : message[..500];
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
