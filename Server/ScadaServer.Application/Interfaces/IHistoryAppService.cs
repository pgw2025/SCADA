using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 历史数据查询服务接口
    /// </summary>
    public interface IHistoryAppService
    {
        /// <summary>
        /// 查询指定变量的最近历史记录（按时间倒序取 limit 条，返回升序）。
        /// </summary>
        /// <param name="variableKey">变量业务键</param>
        /// <param name="limit">返回条数上限（1~10000，默认 100）</param>
        Task<List<HistoryRecordDto>> GetHistoryAsync(string variableKey, int limit);
    }
}
