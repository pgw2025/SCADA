using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IModelVariableAppService
    {
        Task<ModelVariableDto> GetByIdAsync(int id);
        Task<List<ModelVariableDto>> GetListAsync();
        Task<ModelVariableDto> CreateAsync(ModelVariableDto dto);
        Task<ModelVariableDto> UpdateAsync(ModelVariableDto dto);
        Task DeleteAsync(int id);
    }
}

