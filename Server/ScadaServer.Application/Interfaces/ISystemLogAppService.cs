using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface ISystemLogAppService
    {
        Task<SystemLogDto> GetByIdAsync(int id);
        Task<List<SystemLogDto>> GetListAsync();
        Task CreateAsync(SystemLogDto dto);
        Task UpdateAsync(SystemLogDto dto);
        Task DeleteAsync(int id);
    }
}

