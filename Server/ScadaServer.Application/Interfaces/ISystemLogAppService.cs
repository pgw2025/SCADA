using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface ISystemLogAppService
    {
        Task<SystemLogDto?> GetByIdAsync(int id);

        /// <summary>
        /// 分页查询系统日志（分类/级别/关键字/时间段）。
        /// </summary>
        Task<SystemLogPagedResultDto> QueryAsync(SystemLogQueryDto query);

        Task<List<SystemLogDto>> GetListAsync();
        Task CreateAsync(SystemLogDto dto);
        Task UpdateAsync(SystemLogDto dto);
        Task DeleteAsync(int id);

        /// <summary>
        /// 按分类/时间段批量清理日志（供清理接口使用，需显式时间范围）。
        /// 返回删除条数。
        /// </summary>
        Task<int> ClearAsync(string? category, DateTime? startTime, DateTime? endTime);
    }
}
