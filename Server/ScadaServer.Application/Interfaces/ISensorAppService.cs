using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface ISensorAppService
    {
        Task<SensorDto> GetByIdAsync(int id);
        Task<List<SensorDto>> GetListAsync();
        Task CreateAsync(SensorDto dto);
        Task UpdateAsync(SensorDto dto);
        Task DeleteAsync(int id);
    }
}

