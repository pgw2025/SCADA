namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 历史数据维护服务（清理等运维操作）。
    /// <para>与查询服务 <see cref="IHistoryAppService"/> 分离，语义更清晰；scoped 生命周期。</para>
    /// </summary>
    public interface IHistoryMaintenanceAppService
    {
        /// <summary>
        /// 删除 MySQL <c>VariableHistory</c> 表中指定时间（UTC）之前的全部历史数据（分批删除）。
        /// 返回删除总条数。用于历史清理计划任务在「未配置 InfluxDB」时兜底清理 MySQL。
        /// </summary>
        Task<long> CleanupMySqlBeforeAsync(DateTime cutoffUtc, CancellationToken token);
    }
}
