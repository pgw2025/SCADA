namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// SCADA通知服务接口，用于推送变量更新与设备状态到客户端
    /// </summary>
    public interface IScadaNotificationService
    {
        /// <summary>
        /// 通知变量更新
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="variableKey">变量键</param>
        /// <param name="value">变量值</param>
        Task NotifyVariableUpdateAsync(int deviceId, string variableKey, object value);

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
    }
}
