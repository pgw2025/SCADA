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
    }
}
