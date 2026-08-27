using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 报警记录应用服务接口（查询/确认/当前未恢复）。
    /// </summary>
    public interface IAlarmRecordAppService
    {
        /// <summary>
        /// 分页查询报警记录。
        /// </summary>
        Task<AlarmRecordPagedResultDto> QueryAsync(AlarmRecordQueryDto query);

        /// <summary>
        /// 按ID查询单条报警记录。
        /// </summary>
        Task<AlarmRecordDto?> GetByIdAsync(long id);

        /// <summary>
        /// 查询当前未恢复的报警记录（用于前端实时列表初始化）。
        /// </summary>
        Task<List<AlarmRecordDto>> GetActiveAsync();

        /// <summary>
        /// 确认报警记录（设置确认人/时间）。
        /// </summary>
        Task<bool> AckAsync(long id, string ackBy);
    }
}