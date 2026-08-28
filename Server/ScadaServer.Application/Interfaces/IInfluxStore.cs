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
        /// <paramref name="aggregateWindowMs"/> 大于 0 时，按时间窗口对 value 取均值聚合降采样（适合大范围趋势）。
        /// </summary>
        Task<List<HistoryRecordDto>> QueryLatestAsync(
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start = null,
            DateTime? end = null,
            long? aggregateWindowMs = null);

        /// <summary>健康探测（连接测试）</summary>
        Task<(bool Success, long LatencyMs, string Message)> PingAsync();

        /// <summary>对指定（尚未生效的）配置做连接测试，不改变当前生效客户端。</summary>
        Task<(bool Success, long LatencyMs, string Message)> TestConnectionAsync(DatabaseConfig config);
    }
}
