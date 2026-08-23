using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface ISystemConfigAppService
    {
        Task<SystemConfigDto> GetByIdAsync(int id);
        Task<List<SystemConfigDto>> GetListAsync();
        Task CreateAsync(SystemConfigDto dto);
        Task UpdateAsync(SystemConfigDto dto);
        Task DeleteAsync(int id);
    }
}

