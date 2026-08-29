namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 变量写入审计记录器接口。
    /// <para>
    /// 供运行时层（RuntimeManager）在脚本写入、变量绑定联动写入等非 HTTP 路径上
    /// 记录变量值变更审计日志；HTTP 用户写入由 WebApi 层 [AuditLog] 过滤器记录（含操作人/IP），
    /// 不经过本接口，避免同一次写入产生重复日志。
    /// </para>
    /// <para>
    /// 接口定义在 Application 层，实现在 WebApi 层（桥接 SystemLogRecorder 双通道落库）。
    /// </para>
    /// </summary>
    public interface IVariableWriteAuditRecorder
    {
        /// <summary>
        /// 记录一条变量写入审计日志（仅入队，非阻塞，失败不影响写值主业务）。
        /// </summary>
        /// <param name="deviceId">目标设备 ID</param>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="value">写入的值</param>
        /// <param name="source">写入来源描述（如「系统脚本」「变量绑定」）</param>
        /// <param name="success">写入是否成功</param>
        /// <param name="errorMessage">失败原因（成功时为 null）</param>
        Task RecordAsync(int deviceId, string variableKey, object? value, string source, bool success, string? errorMessage);
    }
}
