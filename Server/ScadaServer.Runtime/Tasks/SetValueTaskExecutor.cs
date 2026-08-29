using System.Text.Json;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Runtime.Tasks
{
    /// <summary>
    /// 变量写入执行器：向指定设备的运行时变量写入固定值（经 RuntimeManager 驱动下发）。
    /// <para>参数（ParamsJson）：deviceId（必填）、variableKey（必填）、newValue（数字或布尔）。</para>
    /// </summary>
    public class SetValueTaskExecutor : IScheduledTaskExecutor
    {
        private readonly IRuntimeDeviceManager _runtimeDeviceManager;

        public SetValueTaskExecutor(IRuntimeDeviceManager runtimeDeviceManager)
        {
            _runtimeDeviceManager = runtimeDeviceManager;
        }

        public string Type => ScheduledTaskTypes.SetValue;

        public async Task<string> ExecuteAsync(ScheduledTask task, CancellationToken token)
        {
            var (deviceId, variableKey, value) = ParseParams(task.ParamsJson);

            var (success, error) = await _runtimeDeviceManager.WriteVariableAsync(
                deviceId, variableKey, value, "计划任务");
            if (!success)
            {
                throw new InvalidOperationException(error ?? "变量写入失败");
            }

            return $"已写入设备 {deviceId} 变量 [{variableKey}] = {value}";
        }

        private static (int DeviceId, string VariableKey, object Value) ParseParams(string? paramsJson)
        {
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson).RootElement;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"任务参数不是合法 JSON: {ex.Message}");
            }

            if (!root.TryGetProperty("deviceId", out var deviceIdEl) || deviceIdEl.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException("缺少目标设备参数（deviceId）");
            }
            if (!root.TryGetProperty("variableKey", out var varEl) || string.IsNullOrWhiteSpace(varEl.GetString()))
            {
                throw new InvalidOperationException("缺少目标变量参数（variableKey）");
            }
            if (!root.TryGetProperty("newValue", out var valueEl))
            {
                throw new InvalidOperationException("缺少写入值参数（newValue）");
            }

            object value = valueEl.ValueKind switch
            {
                JsonValueKind.Number => valueEl.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => valueEl.GetString()!,
                _ => throw new InvalidOperationException("写入值（newValue）必须是数字或布尔")
            };

            return (deviceIdEl.GetInt32(), varEl.GetString()!, value);
        }
    }
}
