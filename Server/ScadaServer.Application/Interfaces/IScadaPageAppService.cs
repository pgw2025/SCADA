using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IScadaPageAppService
    {
        Task<ScadaPageDto> GetByIdAsync(int id);
        Task<List<ScadaPageDto>> GetListAsync();
        Task CreateAsync(ScadaPageDto dto);
        Task UpdateAsync(ScadaPageDto dto);
        Task DeleteAsync(int id);
    }
}

