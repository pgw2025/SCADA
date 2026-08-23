namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 变量质量状态枚举
    /// </summary>
    public enum VariableQuality
    {
        /// <summary>
        /// 质量良好，数据有效
        /// </summary>
        Good = 0,

        /// <summary>
        /// 质量差，数据无效
        /// </summary>
        Bad = 1,

        /// <summary>
        /// 数据不确定
        /// </summary>
        Uncertain = 2,

        /// <summary>
        /// 通信错误
        /// </summary>
        CommunicationError = 3,

        /// <summary>
        /// 设备离线
        /// </summary>
        DeviceOffline = 4,

        /// <summary>
        /// 通信超时
        /// </summary>
        Timeout = 5,

        /// <summary>
        /// 未连接
        /// </summary>
        NotConnected = 6,

        /// <summary>
        /// 初始化中
        /// </summary>
        Initializing = 7
    }
}
