using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Processing;

namespace ScadaServer.Runtime.Devices
{
    /// <summary>
    /// 设备工作器，负责单台设备的数据采集和驱动通讯。
    /// 以变量级轮询周期（RuntimeVariable.PollingIntervalMs）驱动采集，支持质量状态管理与平均响应时间计算。
    /// </summary>
    /// <remarks>
    /// 每个设备运行时对应一个 DeviceWorker 实例，由 DeviceScheduler 调度执行。
    /// 采集节奏由每个 RuntimeVariable 的 NextPollTime 决定（各自 PollingIntervalMs），
    /// 不再依赖单一设备级固定延迟。
    /// <para>
    /// 阶段三起：本 Worker 仅保留<b>采集与轮次控制</b>（到期收集 → 批读 → 错误标记映射 →
    /// NextPollTime 推进 → 轮次统计）；读取成功后的值处理（工程换算/锁内内存更新/事件发布/
    /// 通知入队/历史/实时/报警）统一移交 <see cref="IVariableValueProcessor"/>（轮询与订阅共用管线）。
    /// </para>
    /// </remarks>
    public class DeviceWorker
    {
        private readonly DeviceRuntime _runtime;
        private readonly ILogger<DeviceWorker> _logger;
        private readonly IVariableValueProcessor _processor;

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
        /// <param name="processor">变量值处理管线（轮询与订阅共用）</param>
        /// <exception cref="ArgumentNullException">runtime / logger / processor 为 null 时抛出</exception>
        public DeviceWorker(DeviceRuntime runtime, ILogger<DeviceWorker> logger, IVariableValueProcessor processor)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
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

                // 收集本轮到期的变量（按各自 PollingIntervalMs 调度）。
                // Step 4.5：跳过 UpdateMode == Subscription 的变量——订阅变量由驱动推送（OPC UA），
                // 不再轮询读取；混合设备中轮询变量照常按各自间隔读取，纯订阅设备 due 恒空（由下方看门狗接管空转分支）。
                var due = new List<VariableRuntime>();
                foreach (var vr in _runtime.Variables.Values)
                {
                    if (!vr.IsEnabled) continue;
                    if (vr.UpdateMode == UpdateModeEnum.Subscription) continue;
                    if (now >= vr.NextPollTime) due.Add(vr);
                }

                if (due.Count == 0)
                {
                    // 空转分支（Step 4.6）：
                    // 1. 不再无条件置 Connected（P1-1 修复）：连接共享架构下设备连接状态由会话
                    //    挂载/扇出同步，历史 hack 会导致纯订阅设备断线永远显示在线。
                    // 2. 看门狗：节流 5s 探测连接存活——探测死亡才上抛会话级重连归口（会话内再
                    //    探测 + 闸门去重，多设备并发收敛为一次重建）。不使用值陈旧度判断断线：
                    //    OPC UA 仅在值变化时发布数据通知，静态值长时间无回调是正常行为（D5 附注）。
                    await WatchdogAsync(now);

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
                    // 轮次级成功统计：本轮至少一个变量读取成功才视为通讯成功，
                    // 用于收尾时区分"部分成功=在线"与"全部失败=通讯故障"。
                    var anySuccess = false;

                    // P5 批读（性能补偿，连接共享驱动串行化下的吞吐优化）：
                    // 到期变量集合一次 ReadBatchAsync 取回，减少网络往返与驱动内锁竞争。
                    // 整体抛异常 → batch=null，回退逐变量 ReadAsync（保留故障隔离与 LastError）。
                    IDictionary<string, object>? batch = null;
                    try
                    {
                        batch = await _runtime.Driver.ReadBatchAsync(due);
                    }
                    catch
                    {
                        batch = null;
                    }

                    foreach (var vr in due)
                    {
                        try
                        {
                            // P5：优先取批读结果；缺项（整体降级/驱动未返回/错误标记）走单变量补读。
                            // 批读错误标记（S7 READ_ERROR / INVALID_ADDRESS、OPC UA READ_ERROR）——由
                            // IsErrorMarker 识别为 null（走无效值路径），保证"单变量失败不拖垮整轮"语义不变。
                            object? newValue;
                            if (batch != null && batch.TryGetValue(vr.Key, out var batched))
                            {
                                newValue = IsErrorMarker(batched) ? null : batched;
                            }
                            else
                            {
                                newValue = await _runtime.Driver.ReadAsync(vr);
                            }

                            // 值处理统一交管线（工程换算/质量/锁内更新/事件发布/通知入队/历史/实时/报警），
                            // null（含错误标记映射、驱动返回 null）由管线内部走 CommunicationError 降级分支。
                            await _processor.ApplyPolledAsync(_runtime, vr, newValue, now);

                            // 读取成功（非 null 且管线未抛异常）计入轮次级成功统计；
                            // 管线内单段失败（历史/实时/报警）已就地兜底，不会误判为通讯失败。
                            if (newValue != null)
                            {
                                anySuccess = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            // 单个变量读取失败（驱动读取异常；管线未兜底的意外异常）：标记通信错误。
                            // 质量降级通知（好→坏跃迁推送一次）由管线以 null 值重入触发——
                            // 读取异常发生在管线执行前，vr.Quality 未被管线改写，语义与改造前一致。
                            _runtime.LastError = TruncateError(ex.Message);
                            _logger.LogError(ex, "Read variable {VariableName} failed.", vr.Name);
                            try
                            {
                                await _processor.ApplyPolledAsync(_runtime, vr, null, now);
                            }
                            catch (Exception innerEx)
                            {
                                _logger.LogDebug(innerEx, "变量 {VariableName} 读取失败后的质量降级通知失败（已忽略）。", vr.Name);
                            }
                        }
                        finally
                        {
                            // 无论成功或失败，均推进该变量下一次轮询时间
                            vr.NextPollTime = now.AddMilliseconds(vr.PollingIntervalMs);
                        }
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
                    else if (HasFreshSubscriptionEvidence())
                    {
                        // Step 4.7（P1-2 主防线）：订阅健康证据检查——设备存在启用订阅变量且最近
                        // 订阅打点（回调）新鲜，视为本轮通讯有证据（订阅推送在值无变化时本就不会产生回调）。
                        // 该场景轮询全失败通常是坏地址（设备级故障），不置 Error、不递增失败计数、
                        // 不触发会话重建；真实连接死亡时订阅同样静默（无打点）→ 由空转看门狗兜底。
                        // 残留行为记录：轮次仍记录失败明细日志（本质是配置错误，可见的故障呈现有助排障）。
                        _runtime.LastError = TruncateError(
                            $"本轮 {due.Count} 个到期变量全部读取失败（订阅路径健康，判定为设备级配置问题，不触发断线重连）");
                        if (_runtime.ConsecutiveFailureCount == 0)
                        {
                            _logger.LogWarning(
                                "Device {DeviceKey} 本轮 {Count} 个到期变量全部读取失败，但订阅路径健康（判定为坏地址/配置问题，不触发会话重建）。",
                                _runtime.Device.Key, due.Count);
                        }
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
                            // 连接级重连归口：将断线信号上抛所属会话（fire-and-forget，异常兜底）。
                            // 会话侧先 IsAliveAsync 探测：连接存活则判定设备级故障不重建；死亡则进入会话重建
                            //（多设备并发触发经会话闸门去重收敛为一次重建）。
                            if (_runtime.Session != null)
                            {
                                try
                                {
                                    await _runtime.Session.SignalConnectionFailureAsync(_runtime.Device.Id);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug(ex, "设备 {DeviceKey} 断线信号上抛会话失败（已忽略，继续按设备级重连退出）。",
                                        _runtime.Device.Key);
                                }
                            }
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

        /// <summary>失败原因截断（上限 500 字符），防止异常消息超长撑爆快照/日志。</summary>
        private static string? TruncateError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;
            return message.Length <= 500 ? message : message[..500];
        }

        // ===================== 空转看门狗（Step 4.6） =====================

        /// <summary>看门狗探测间隔（毫秒）：节流探测，避免空转分支高频 IsAliveAsync 调用。</summary>
        private const int WatchdogIntervalMs = 5000;

        /// <summary>下次看门狗探测时刻（Worker 实例字段，单实例周期运行）。</summary>
        private DateTime _nextWatchdogTime = DateTime.MinValue;

        /// <summary>
        /// 空转看门狗：节流探测连接存活，探测死亡时上抛会话级重连归口
        /// （会话内再探测 + 闸门去重 → 死亡才会话重建，多设备并发收敛为一次）。
        /// 探测异常仅记 Debug——看门狗本身不得触发重连风暴。
        /// </summary>
        private async Task WatchdogAsync(DateTime now)
        {
            if (now < _nextWatchdogTime) return;
            _nextWatchdogTime = now.AddMilliseconds(WatchdogIntervalMs);

            var driver = _runtime.Driver;
            if (driver == null) return;

            try
            {
                if (!await driver.IsAliveAsync())
                {
                    _logger.LogWarning("Device {DeviceKey} 空转看门狗探测连接不存活，上抛会话级重连判定。",
                        _runtime.Device.Key);
                    if (_runtime.Session != null)
                    {
                        await _runtime.Session.SignalConnectionFailureAsync(_runtime.Device.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Device {DeviceKey} 空转看门狗探测异常（已忽略）。", _runtime.Device.Key);
            }
        }

        // ===================== 订阅健康证据（Step 4.7） =====================

        /// <summary>
        /// 订阅健康证据判定：设备存在启用订阅变量，且最近订阅打点（回调）在新鲜度阈值内。
        /// 新鲜度阈值 = max(30s, 3 × 订阅变量最大采样间隔)——静态值无回调是订阅推送的正常行为，
        /// 阈值需覆盖完整采样周期。本方法无网络 IO，仅读内存字段。
        /// </summary>
        private bool HasFreshSubscriptionEvidence()
        {
            var hasSubscription = false;
            var maxIntervalMs = 0;
            foreach (var vr in _runtime.Variables.Values)
            {
                if (!vr.IsEnabled || vr.UpdateMode != UpdateModeEnum.Subscription) continue;
                hasSubscription = true;
                if (vr.PollingIntervalMs > maxIntervalMs) maxIntervalMs = vr.PollingIntervalMs;
            }
            if (!hasSubscription) return false;

            var last = _runtime.LastCommunicationTime;
            if (last == null) return false;

            var freshnessMs = Math.Max(30_000, 3L * maxIntervalMs);
            return (DateTime.UtcNow - last.Value).TotalMilliseconds < freshnessMs;
        }

        /// <summary>
        /// 批读错误标记判定：S7Driver / OpcUaDriver 以<b>非 null 字符串</b>标记单变量失败
        /// （S7 的 <c>READ_ERROR</c> / <c>INVALID_ADDRESS</c>、OPC UA 的 <c>READ_ERROR</c>，见各自驱动 ReadBatchAsync 契约）。
        /// Worker 据此将标记映射为 null（走无效值路径），保证「单变量失败不拖垮整轮」语义与逐变量路径一致。
        /// 仅识别约定标记字符串，真实字符串变量值不受影响。
        /// </summary>
        private static bool IsErrorMarker(object value)
        {
            return value is string s &&
                   (s == "READ_ERROR" || s == "INVALID_ADDRESS");
        }
    }
}
