namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 历史存储模式枚举（替代原 string StoreMode + bool IsStored 组合）
    /// None 等价于"不存储历史数据"，其余为具体的存储策略。
    /// </summary>
    public enum StoreModeEnum
    {
        /// <summary>
        /// 不存储历史数据
        /// </summary>
        None,

        /// <summary>
        /// 变化存储（值变化时写入）
        /// </summary>
        Change,

        /// <summary>
        /// 周期存储（按采集周期定时写入）
        /// </summary>
        Cycle,

        /// <summary>
        /// 压缩存储（历史趋势压缩）
        /// </summary>
        Compressed,

        /// <summary>
        /// 聚合存储（按时间窗口聚合后写入）
        /// </summary>
        Aggregated
    }
}
