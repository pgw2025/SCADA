using ScadaServer.Domain.Enums;
using ScadaServer.Runtime;
using ScadaServer.Runtime.Devices;

namespace ScadaServer.Runtime.Scripting
{
    /// <summary>
    /// 脚本读写桥：按 (DeviceKey, VariableKey) 二元寻址，经由 RuntimeManager 访问运行时变量。
    /// 负责解析设备键 → 设备 ID，读取变量当前值/质量，写入变量。
    /// 授权判定（IsReadAllowed / IsWriteAllowed）供 ScriptSandbox 桥接层在 read/write 前调用；本类只做能力映射。
    /// </summary>
    public sealed class ScriptRuntimeAccess
    {
        private readonly RuntimeManager _runtime;

        public ScriptRuntimeAccess(RuntimeManager runtime)
        {
            _runtime = runtime;
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
        /// </summary>
        public bool Write(string deviceKey, string variableKey, object value, out string errorMessage)
        {
            var device = FindDevice(deviceKey);
            if (device == null)
            {
                errorMessage = $"设备 [{deviceKey}] 不在运行中";
                return false;
            }

            var (success, error) = _runtime.WriteVariableAsync(device.Device.Id, variableKey, value).GetAwaiter().GetResult();
            errorMessage = error ?? string.Empty;
            return success;
        }
    }
}