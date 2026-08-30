namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// SCADA通知服务接口，用于推送变量更新与设备状态到客户端
    /// </summary>
    public interface IScadaNotificationService
    {
        /// <summary>
        /// 通知变量更新（结构化载荷：值 + 质量状态 + 采集时间）。
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="variableKey">变量键</param>
        /// <param name="value">变量值（读取失败推送质量降级时可能为 null，表示无有效值）</param>
        /// <param name="quality">变量质量（Good / Bad / Uncertain / CommunicationError 等）</param>
        /// <param name="updateTime">采集时间（UTC，项目时间戳约定）</param>
        Task NotifyVariableUpdateAsync(int deviceId, string variableKey, object? value, Domain.Enums.VariableQuality quality, DateTime updateTime);

        /// <summary>
        /// 通知设备运行时状态变更
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="status">对外设备状态</param>
        Task NotifyDeviceStatusAsync(int deviceId, Domain.Enums.DeviceStatus status);

        /// <summary>
        /// 通知系统报警（变量越界 / 报警规则命中等）。
        /// <para>
        /// 供运行时报警检测（如变量上下限越界）调用，前端通过 SignalR "ReceiveSystemAlarm" 接收展示。
        /// </para>
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="variableKey">变量键</param>
        /// <param name="variableName">变量名称</param>
        /// <param name="message">报警描述</param>
        /// <param name="level">报警级别（如 High / Low / Error）</param>
        Task NotifySystemAlarmAsync(int deviceId, string variableKey, string variableName, string message, string level);

        /// <summary>
        /// 通知结构化报警事件（触发/恢复）。
        /// <para>
        /// 供运行时报警检测（规则引擎命中或 Min/Max 兜底、系统级）调用，
        /// 前端通过 SignalR "ReceiveAlarm" 接收同构对象做实时列表、角标与确认态展示。
        /// </para>
        /// </summary>
        /// <param name="evt">报警事件（含设备/变量/级别/数值/来源等信息）</param>
        Task NotifyAlarmAsync(DTOs.AlarmEvent evt);

        /// <summary>
        /// 通知脚本执行事件（前端控制台 + 状态角标实时刷新）。SignalR "ReceiveScriptExecution"。
        /// </summary>
        Task NotifyScriptExecutionAsync(DTOs.ScriptExecutionEvent evt);
    }
}
