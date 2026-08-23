using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Runtime.Devices
{
    /// <summary>
    /// 设备工作器，负责单台设备的数据采集和驱动通讯
    /// </summary>
    /// <remarks>
    /// 每个设备运行时对应一个 DeviceWorker 实例，由 DeviceScheduler 调度执行。
    /// 工作器以轮询方式周期性地读取设备所有变量的值，并更新运行时状态和统计信息。
    /// 支持变量变化检测、质量状态管理和平均响应时间计算。
    /// </remarks>
    public class DeviceWorker
    {
        private readonly DeviceRuntime _runtime;
        private readonly ILogger<DeviceWorker> _logger;

        /// <summary>
        /// 初始化设备工作器
        /// </summary>
        /// <param name="runtime">设备运行时，包含设备配置、驱动实例和变量集合</param>
        /// <param name="logger">日志记录器</param>
        /// <exception cref="ArgumentNullException">runtime 或 logger 为 null 时抛出</exception>
        public DeviceWorker(DeviceRuntime runtime, ILogger<DeviceWorker> logger)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                try
                {
                    // 遍历读取该设备下所有变量
                    foreach (var variable in _runtime.Variables.Values)
                    {
                        try
                        {
                            // 通过驱动读取变量当前值
                            var newValue = await _runtime.Driver.ReadAsync(variable.Variable);

                            // 更新变量值和状态
                            variable.PreviousValue = variable.Value;
                            variable.Value = newValue;
                            variable.UpdateTime = DateTime.Now;
                            variable.Quality = VariableQuality.Good;

                            // 检测值是否发生变化
                            variable.IsChanged = !Equals(variable.Value, variable.PreviousValue);
                        }
                        catch (Exception ex)
                        {
                            // 单个变量读取失败，标记通信错误但不中断其他变量
                            variable.Quality = VariableQuality.CommunicationError;
                            _logger.LogError(ex, "Read variable {VariableName} failed.", variable.Variable.Name);
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
                    // 更新平均响应时间（基于成功次数的移动平均）
                    sw.Stop();
                    _runtime.AverageResponseTime =
                        (_runtime.AverageResponseTime * (_runtime.SuccessCount - 1) + sw.Elapsed.TotalMilliseconds)
                        / _runtime.SuccessCount;
                }

                // 等待下一个轮询周期，间隔由设备配置决定
                await Task.Delay(_runtime.Device.PollingInterval, cancellationToken);
            }

            // 循环结束，标记设备断开
            _runtime.ConnectionState = DeviceConnectionState.Disconnected;
            _logger.LogInformation("DeviceWorker {DeviceKey} stopped.", _runtime.Device.Key);
        }
    }
}