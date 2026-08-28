namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 历史数据记录器接口。
    /// <para>
    /// 供运行时采集层调用，将采样点异步入队，由后台服务批量落库，
    /// 避免在采集循环内同步写数据库阻塞采集。
    /// </para>
    /// </summary>
    public interface IHistoryRecorder
    {
        /// <summary>
        /// 记录一个采样点（非阻塞入队；队列满时丢弃并告警计数）。
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="deviceKey">设备标识</param>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="variableName">变量名称</param>
        /// <param name="value">数值化后的值（非数值型变量按 0/1 处理）</param>
        /// <param name="rawValue">原始值字符串</param>
        /// <param name="quality">采样质量（如 Good / CommunicationError）</param>
        /// <param name="sampleTime">采样时刻（设备采集时间，而非入队/落库时间）</param>
        void Record(
            int deviceId,
            string deviceKey,
            string variableKey,
            string variableName,
            double value,
            string? rawValue,
            string? quality,
            DateTime sampleTime);

        /// <summary>
        /// 标记不再有新数据（关闭通道，触发后台排空剩余数据）。
        /// </summary>
        void Complete();
    }
}
