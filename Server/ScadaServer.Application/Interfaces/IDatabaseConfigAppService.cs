using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IDatabaseConfigAppService
    {
        Task<DatabaseConfigDto> GetByIdAsync(int id);
        Task<List<DatabaseConfigDto>> GetListAsync();
        Task CreateAsync(DatabaseConfigDto dto);
        Task UpdateAsync(DatabaseConfigDto dto);
        Task DeleteAsync(int id);
    }
}

