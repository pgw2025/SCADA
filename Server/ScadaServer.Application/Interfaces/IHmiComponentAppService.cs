using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IHmiComponentAppService
    {
        Task<HmiComponentDto> GetByIdAsync(int id);
        Task<List<HmiComponentDto>> GetListAsync();
        Task CreateAsync(HmiComponentDto dto);
        Task UpdateAsync(HmiComponentDto dto);
        Task DeleteAsync(int id);
    }
}

