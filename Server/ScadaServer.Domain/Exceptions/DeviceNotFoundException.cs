namespace ScadaServer.Domain.Exceptions
{
    /// <summary>
    /// 设备未找到异常
    /// </summary>
    public class DeviceNotFoundException : Exception
    {
        /// <summary>
        /// 初始化设备未找到异常
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        public DeviceNotFoundException(int deviceId)
            : base($"Device with ID {deviceId} was not found.") { }
    }
}
