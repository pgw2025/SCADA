namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 变量更新模式枚举
    /// </summary>
    public enum UpdateMode
    {
        /// <summary>
        /// 轮询模式（主动定时获取）
        /// </summary>
        Polling,

        /// <summary>
        /// 订阅模式（被动接收推送）
        /// </summary>
        Subscription
    }
}
