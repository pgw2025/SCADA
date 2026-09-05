namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 设备变量更新方式。
    /// </summary>
    public enum UpdateModeEnum
    {
        /// <summary>
        /// 自主轮询：由 DeviceWorker 按轮询间隔主动读取。
        /// </summary>
        Polling = 0,

        /// <summary>
        /// 订阅推送：由驱动（OPC UA）在值变化时回调推送。
        /// </summary>
        Subscription = 1
    }
}
