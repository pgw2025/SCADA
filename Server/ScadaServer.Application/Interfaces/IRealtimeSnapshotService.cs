namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 实时快照服务接口（MySQL 实时库快照写入）。
    /// <para>
    /// 采集循环每次成功读到变量后调用 <see cref="Update"/> 更新内存快照（非阻塞），
    /// 后台由实现方周期性批量 Upsert 到 VariableRealtime 表。
    /// </para>
    /// </summary>
    public interface IRealtimeSnapshotService
    {
        /// <summary>
        /// 更新某设备某变量的最新实时快照（内存态，返回立即）。
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="deviceKey">设备标识</param>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="variableName">变量名称</param>
        /// <param name="value">数值化后的值</param>
        /// <param name="rawValue">原始值字符串</param>
        /// <param name="quality">采样质量（如 Good / CommunicationError）</param>
        /// <param name="timestamp">采样时间</param>
        void Update(
            int deviceId,
            string deviceKey,
            string variableKey,
            string variableName,
            double value,
            string? rawValue,
            string? quality,
            DateTime timestamp);
    }
}
