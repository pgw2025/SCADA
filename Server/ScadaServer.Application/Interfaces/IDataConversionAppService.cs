using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IDataConversionAppService
    {
        Task<DataConversionDto> GetByIdAsync(int id);
        Task<List<DataConversionDto>> GetListAsync();
        Task CreateAsync(DataConversionDto dto);
        Task UpdateAsync(DataConversionDto dto);
        Task DeleteAsync(int id);
    }
}

