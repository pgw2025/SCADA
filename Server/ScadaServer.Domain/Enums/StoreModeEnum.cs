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
        /// 周期存储（按存储周期 <c>StoreIntervalMs</c> 定时写入，与轮询采集间隔解耦）
        /// </summary>
        Cycle,

        /// <summary>
        /// 压缩存储（历史趋势压缩）
        /// <para>当前实现暂等同 <see cref="Cycle"/>：按 <c>StoreIntervalMs</c> 周期写入原始点，真正的压缩算法留待后续。</para>
        /// </summary>
        Compressed,

        /// <summary>
        /// 聚合存储（按时间窗口聚合后写入）
        /// <para>当前实现暂等同 <see cref="Cycle"/>：按 <c>StoreIntervalMs</c> 周期写入原始点，真正的窗口聚合留待后续。</para>
        /// </summary>
        Aggregated
    }
}
