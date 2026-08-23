using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IExposedInterfaceAppService
    {
        Task<ExposedInterfaceDto> GetByIdAsync(int id);
        Task<List<ExposedInterfaceDto>> GetListAsync();
        Task CreateAsync(ExposedInterfaceDto dto);
        Task UpdateAsync(ExposedInterfaceDto dto);
        Task DeleteAsync(int id);
    }
}

