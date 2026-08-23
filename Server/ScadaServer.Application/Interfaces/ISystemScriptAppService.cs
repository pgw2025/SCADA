using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface ISystemScriptAppService
    {
        Task<SystemScriptDto> GetByIdAsync(int id);
        Task<List<SystemScriptDto>> GetListAsync();
        Task CreateAsync(SystemScriptDto dto);
        Task UpdateAsync(SystemScriptDto dto);
        Task DeleteAsync(int id);
    }
}

