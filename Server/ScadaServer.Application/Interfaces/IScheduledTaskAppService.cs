using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IScheduledTaskAppService
    {
        Task<ScheduledTaskDto> GetByIdAsync(int id);
        Task<List<ScheduledTaskDto>> GetListAsync();
        Task CreateAsync(ScheduledTaskDto dto);
        Task UpdateAsync(ScheduledTaskDto dto);
        Task DeleteAsync(int id);
    }
}

