namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 设备运行状态枚举
    /// </summary>
    public enum DeviceStatus
    {
        /// <summary>
        /// 离线
        /// </summary>
        Offline = 0,

        /// <summary>
        /// 在线
        /// </summary>
        Online = 1,

        /// <summary>
        /// 故障
        /// </summary>
        Fault = 2,

        /// <summary>
        /// 配置更新中
        /// </summary>
        ConfigUpdating = 3,

        /// <summary>
        /// 正在连接
        /// </summary>
        Connecting = 4
    }
}
