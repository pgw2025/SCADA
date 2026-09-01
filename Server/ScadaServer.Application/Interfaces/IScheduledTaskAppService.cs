using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 计划任务应用服务：管理定时/周期性计划任务的增删改查。
    /// </summary>
    public interface IScheduledTaskAppService
    {
        /// <summary>按ID查询单个计划任务；不存在返回 null。</summary>
        Task<ScheduledTaskDto?> GetByIdAsync(int id);

        /// <summary>查询全部计划任务。</summary>
        Task<List<ScheduledTaskDto>> GetListAsync();

        /// <summary>新增一个计划任务。</summary>
        Task CreateAsync(ScheduledTaskDto dto);

        /// <summary>更新一个计划任务。</summary>
        Task UpdateAsync(ScheduledTaskDto dto);

        /// <summary>删除一个计划任务。</summary>
        Task DeleteAsync(int id);
    }
}

