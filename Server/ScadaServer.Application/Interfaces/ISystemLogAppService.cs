using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 系统日志应用服务：管理系统日志记录的增删改查、分页查询与批量清理。
    /// </summary>
    public interface ISystemLogAppService
    {
        /// <summary>按ID查询单条系统日志；不存在返回 null。</summary>
        Task<SystemLogDto?> GetByIdAsync(int id);

        /// <summary>
        /// 分页查询系统日志（分类/级别/关键字/时间段）。
        /// </summary>
        Task<SystemLogPagedResultDto> QueryAsync(SystemLogQueryDto query);

        /// <summary>查询全部系统日志。</summary>
        Task<List<SystemLogDto>> GetListAsync();

        /// <summary>新增一条系统日志。</summary>
        Task CreateAsync(SystemLogDto dto);

        /// <summary>更新一条系统日志。</summary>
        Task UpdateAsync(SystemLogDto dto);

        /// <summary>删除一条系统日志。</summary>
        Task DeleteAsync(int id);

        /// <summary>
        /// 按分类/时间段批量清理日志（供清理接口使用，需显式时间范围）。
        /// 返回删除条数。
        /// </summary>
        /// <param name="category">日志分类（可为 null，表示全部）</param>
        /// <param name="startTime">起始时间（可为 null）</param>
        /// <param name="endTime">结束时间（可为 null）</param>
        /// <returns>被删除的日志条数</returns>
        Task<int> ClearAsync(string? category, DateTime? startTime, DateTime? endTime);
    }
}
