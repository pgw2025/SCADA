using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// InfluxDB 时序库访问抽象（历史数据存储通道）。
    /// <para>
    /// 单例常驻；<see cref="Rebuild"/> 在历史库配置变更时重建客户端实现热切换。
    /// 查询身份 = device_key + variable_key 二元组（变量名可跨设备重名）。
    /// </para>
    /// </summary>
    public interface IInfluxStore
    {
        /// <summary>是否已存在生效的历史库配置（可用）</summary>
        bool IsConfigured { get; }

        /// <summary>按生效历史库配置重建客户端（配置变更热切换；内部加锁）</summary>
        void Rebuild(DatabaseConfig config);

        /// <summary>批量写入历史采样点（内部有限重试）；返回是否成功，失败由调用方决定回退</summary>
        Task<bool> WriteAsync(List<VariableHistory> points);

        /// <summary>
        /// 查询指定设备+变量的历史记录（按时间升序返回）。
        /// <paramref name="start"/>/<paramref name="end"/> 为空时取最近 limit 条。
        /// <paramref name="aggregateWindowMs"/> 大于 0 时，按时间窗口对 value 聚合降采样（适合大范围趋势）。
        /// <paramref name="aggregateFn"/> 聚合函数（mean/max/min/first/last，默认 mean）。
        /// </summary>
        Task<List<HistoryRecordDto>> QueryLatestAsync(
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start = null,
            DateTime? end = null,
            long? aggregateWindowMs = null,
            string aggregateFn = "mean");

        /// <summary>健康探测（连接测试）</summary>
        Task<(bool Success, long LatencyMs, string Message)> PingAsync();

        /// <summary>对指定（尚未生效的）配置做连接测试，不改变当前生效客户端。</summary>
        Task<(bool Success, long LatencyMs, string Message)> TestConnectionAsync(DatabaseConfig config);

        /// <summary>
        /// 删除 variable_history 测量中指定时间（UTC）之前的全部时序点（历史清理任务用）。
        /// 未配置 InfluxDB 时返回 (false, 提示)。
        /// </summary>
        Task<(bool Success, string Message)> DeleteBeforeAsync(DateTime cutoffUtc);

        /// <summary>
        /// 全量导出 variable_history 测量为 CSV 文件（备份任务用，InfluxDB 原生 CSV 格式）。
        /// 返回是否成功与数据行数；未配置 InfluxDB 时返回失败。
        /// </summary>
        Task<(bool Success, long Rows, string Message)> ExportAllAsync(string outputCsvPath);
    }
}
