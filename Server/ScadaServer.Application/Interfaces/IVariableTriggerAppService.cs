using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IVariableTriggerAppService
    {
        Task<VariableTriggerDto> GetByIdAsync(int id);
        Task<List<VariableTriggerDto>> GetListAsync();
        Task CreateAsync(VariableTriggerDto dto);
        Task UpdateAsync(VariableTriggerDto dto);
        Task DeleteAsync(int id);
    }
}

