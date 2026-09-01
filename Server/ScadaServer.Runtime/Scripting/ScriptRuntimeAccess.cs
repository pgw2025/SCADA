using System.Threading;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime;
using ScadaServer.Runtime.Devices;

namespace ScadaServer.Runtime.Scripting
{
    /// <summary>
    /// 脚本读写桥：按 (DeviceKey, VariableKey) 二元寻址，经由 RuntimeManager 访问运行时变量。
    /// 负责解析设备键 → 设备 ID，读取变量当前值/质量，写入变量。
    /// 授权判定（IsReadAllowed / IsWriteAllowed）供 ScriptSandbox 桥接层在 read/write 前调用；本类只做能力映射。
    /// <para>
    /// write 为同步阻塞语义（Jint 无异步 API），但等待有上界（WriteBridgeTimeoutMs）：
    /// 防止 PLC 网络 IO 无界挂起使脚本执行线程永久挂死——Jint 的 TimeoutInterval 只在脚本
    /// 指令边界检查时钟，委托内部的阻塞期间无法触发超时。超时后向脚本返回 false，
    /// 底层写入以孤儿任务继续，最终结果经 ContinueWith 记录日志（结果以写入审计日志为准）。
    /// </para>
    /// </summary>
    public sealed class ScriptRuntimeAccess
    {
        private readonly RuntimeManager _runtime;

        /// <summary>写桥同步等待上界（毫秒），建议 ≥ 设备写超时（Devices:WriteTimeoutMs）。</summary>
        private readonly int _writeBridgeTimeoutMs;

        /// <summary>可选日志（由 ScriptEngineHost 注入）；记录写桥超时与孤儿任务迟到落地。</summary>
        private readonly ILogger? _logger;

        /// <summary>写桥超时累计计数（供宿主周期性观测日志读取）。</summary>
        private long _bridgeTimeoutCount;

        /// <summary>写桥超时累计次数。</summary>
        public long BridgeTimeoutCount => Interlocked.Read(ref _bridgeTimeoutCount);

        /// <summary>脚本写桥默认同步等待上界（毫秒）。</summary>
        public const int DefaultWriteBridgeTimeoutMs = 6000;

        public ScriptRuntimeAccess(RuntimeManager runtime, int writeBridgeTimeoutMs = DefaultWriteBridgeTimeoutMs, ILogger? logger = null)
        {
            _runtime = runtime;
            _writeBridgeTimeoutMs = Math.Clamp(writeBridgeTimeoutMs, 500, 60000);
            _logger = logger;
        }

        private DeviceRuntime? FindDevice(string deviceKey)
        {
            foreach (var runtime in _runtime.DeviceRuntimes.Values)
            {
                if (string.Equals(runtime.Device.Key, deviceKey, StringComparison.Ordinal))
                {
                    return runtime;
                }
            }
            return null;
        }

        private VariableRuntime? FindVariable(DeviceRuntime device, string variableKey)
        {
            foreach (var vr in device.Variables.Values)
            {
                if (string.Equals(vr.Key, variableKey, StringComparison.Ordinal))
                {
                    return vr;
                }
            }
            return null;
        }

        /// <summary>
        /// 读取变量当前值（未找到返回 null）。
        /// </summary>
        public object? Read(string deviceKey, string variableKey)
        {
            var device = FindDevice(deviceKey);
            if (device == null)
            {
                return null;
            }
            return FindVariable(device, variableKey)?.Value;
        }

        /// <summary>
        /// 读取变量质量（Good/Bad/Uncertain/Unknown）。设备或变量不存在时返回 "Unknown"。
        /// </summary>
        public string GetQuality(string deviceKey, string variableKey)
        {
            var device = FindDevice(deviceKey);
            if (device == null)
            {
                return "Unknown";
            }
            return FindVariable(device, variableKey)?.Quality.ToString() ?? "Unknown";
        }

        /// <summary>
        /// 判定设备级读授权。授权串为 ';' 分隔的设备键；空 = 拒绝。
        /// </summary>
        public static bool IsReadAllowed(string? scopeRead, string deviceKey)
        {
            if (string.IsNullOrWhiteSpace(scopeRead))
            {
                return false;
            }
            var set = scopeRead.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return set.Contains(deviceKey, StringComparer.Ordinal);
        }

        /// <summary>
        /// 判定变量级写授权（条目格式 "设备键.变量键"）。空 = 拒绝。
        /// </summary>
        public static bool IsWriteAllowed(string? scopeWrite, string deviceKey, string variableKey)
        {
            if (string.IsNullOrWhiteSpace(scopeWrite))
            {
                return false;
            }
            var target = $"{deviceKey}.{variableKey}";
            var set = scopeWrite.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return set.Contains(target, StringComparer.Ordinal);
        }

        /// <summary>
        /// 写变量。成功返回 true；失败返回 false 并通过 <paramref name="errorMessage"/> 说明原因。
        /// <para>
        /// 同步等待有上界（<see cref="_writeBridgeTimeoutMs"/>）：超时返回 false，
        /// 底层写入以孤儿任务继续并记录最终结果（见类注释）。等待上界应配置为
        /// ≥ 设备写超时，使 RuntimeManager 的超时文案先于桥接超时返回（语义更准确）。
        /// </para>
        /// </summary>
        public bool Write(string deviceKey, string variableKey, object value, out string errorMessage)
        {
            var device = FindDevice(deviceKey);
            if (device == null)
            {
                errorMessage = $"设备 [{deviceKey}] 不在运行中";
                return false;
            }

            var writeTask = _runtime.WriteVariableAsync(device.Device.Id, variableKey, value, "系统脚本");
            try
            {
                var (success, error) = writeTask
                    .WaitAsync(TimeSpan.FromMilliseconds(_writeBridgeTimeoutMs))
                    .GetAwaiter().GetResult();
                errorMessage = error ?? string.Empty;
                return success;
            }
            catch (TimeoutException)
            {
                Interlocked.Increment(ref _bridgeTimeoutCount);
                errorMessage = $"写入超时（>{_writeBridgeTimeoutMs}ms）：底层写入仍在进行，最终结果以写入审计日志为准";

                // 观察孤儿任务最终结果：同时避免其异常成为"未观察任务异常"。
                _ = writeTask.ContinueWith(t => ObserveOrphanWrite(t, deviceKey, variableKey),
                    TaskScheduler.Default);
                return false;
            }
        }

        /// <summary>记录孤儿写入任务的迟到结果（超时放弃等待后，底层写入可能成功或失败落地）。</summary>
        private void ObserveOrphanWrite(Task<(bool Success, string? ErrorMessage)> task, string deviceKey, string variableKey)
        {
            try
            {
                if (task.IsFaulted)
                {
                    _logger?.LogWarning(task.Exception,
                        "脚本写桥超时后的底层写入最终失败：{DeviceKey}.{VariableKey}", deviceKey, variableKey);
                }
                else if (task.IsCompleted)
                {
                    var (success, error) = task.Result;
                    _logger?.LogWarning(
                        "脚本写桥超时后的底层写入迟到落地：{DeviceKey}.{VariableKey} → {Result}{Error}",
                        deviceKey, variableKey, success ? "成功" : "失败",
                        success ? string.Empty : $"（{error}）");
                }
            }
            catch
            {
                // 观测路径本身不得抛出（ContinueFrom 上下文无调用方兜底）。
            }
        }
    }
}
