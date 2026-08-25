using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;

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

        /// <summary>
        /// 初始化设备工作器
        /// </summary>
        /// <param name="runtime">设备运行时，包含设备配置、驱动实例和变量集合</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="notificationService">变量更新通知服务（SignalR / MQTT）</param>
        /// <param name="historyRecorder">历史数据记录器（异步落库）</param>
        /// <exception cref="ArgumentNullException">runtime 或 logger 为 null 时抛出</exception>
        public DeviceWorker(DeviceRuntime runtime, ILogger<DeviceWorker> logger, IScadaNotificationService notificationService, IHistoryRecorder historyRecorder)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _historyRecorder = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
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

            _runtime.ConnectionState = DeviceConnectionState.Initializing;
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

                            // 更新变量值和状态
                            vr.PreviousValue = vr.Value;
                            vr.Value = newValue;
                            vr.UpdateTime = now;
                            vr.Quality = VariableQuality.Good;

                            // 检测值是否发生变化
                            vr.IsChanged = !Equals(vr.Value, vr.PreviousValue);
                            if (vr.IsChanged && vr.Value != null)
                            {
                                changed.Add((vr.Key, vr.Value));
                            }

                            // 按变量存储策略记录历史采样点（异步入队，不阻塞采集）
                            TryRecordHistory(vr);
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
    }
}
