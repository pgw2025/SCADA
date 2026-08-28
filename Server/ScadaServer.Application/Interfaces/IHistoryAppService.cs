using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 历史数据查询服务接口
    /// </summary>
    public interface IHistoryAppService
    {
        /// <summary>
        /// 查询指定设备下某变量的历史记录（按时间升序返回）。
        /// </summary>
        /// <param name="deviceKey">设备标识；可为空（跨设备查询，兼容旧调用）</param>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="limit">返回条数上限（1~10000，默认 100）</param>
        /// <param name="start">起始时间（UTC，可选）</param>
        /// <param name="end">结束时间（UTC，可选）</param>
        /// <param name="aggregateWindowMs">聚合窗口（毫秒，可选）。>0 时按窗口均值聚合降采样，适合大时间范围。</param>
        Task<List<HistoryRecordDto>> GetHistoryAsync(
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start = null,
            DateTime? end = null,
            long? aggregateWindowMs = null);
    }
}
