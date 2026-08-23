namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 设备连接状态枚举
    /// </summary>
    public enum DeviceConnectionState
    {
        /// <summary>
        /// 未知状态
        /// </summary>
        Unknown,

        /// <summary>
        /// 正在连接中
        /// </summary>
        Connecting,

        /// <summary>
        /// 已连接
        /// </summary>
        Connected,

        /// <summary>
        /// 已断开
        /// </summary>
        Disconnected,

        /// <summary>
        /// 连接错误
        /// </summary>
        Error,

        /// <summary>
        /// 初始化中
        /// </summary>
        Initializing
    }
}
