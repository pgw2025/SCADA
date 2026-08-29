using ScadaServer.Application.Interfaces;
using ScadaServer.WebApi.HostedServices;

namespace ScadaServer.WebApi.Services
{
    /// <summary>
    /// 变量写入审计记录器实现。
    /// <para>
    /// 将运行时层（脚本/绑定联动）的变量写入转译为操作日志，经 <see cref="SystemLogRecorder.RecordOperation"/>
    /// 走无界通道（审计凭据不丢弃）落库。注册为 Singleton（RuntimeManager 为 Singleton）。
    /// </para>
    /// </summary>
    public sealed class VariableWriteAuditRecorder : IVariableWriteAuditRecorder
    {
        /// <summary>写入值在日志内容中的最大长度（超长截断，防止大对象撑爆日志）。</summary>
        private const int MaxValueLength = 100;

        private readonly SystemLogRecorder _recorder;

        public VariableWriteAuditRecorder(SystemLogRecorder recorder)
        {
            _recorder = recorder;
        }

        /// <inheritdoc/>
        public Task RecordAsync(int deviceId, string variableKey, object? value, string source, bool success, string? errorMessage)
        {
            var valueText = FormatValue(value);
            var content = success
                ? $"变量值变更：设备 {deviceId} 变量 [{variableKey}] 被 [{source}] 写入，值 = {valueText}"
                : $"变量写入失败：设备 {deviceId} 变量 [{variableKey}] 由 [{source}] 写入失败，值 = {valueText}，原因：{errorMessage ?? "未知"}";

            var level = success ? "Information" : "Warning";
            _recorder.RecordOperation("Operation", level, "变量写入", "WRITE", source, null, deviceId.ToString(), content);
            return Task.CompletedTask;
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
            {
                return "null";
            }

            var text = value.ToString() ?? string.Empty;
            return text.Length <= MaxValueLength ? text : text[..MaxValueLength] + "…";
        }
    }
}
