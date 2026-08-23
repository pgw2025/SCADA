using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IMqttServerAppService
    {
        Task<MqttServerDto> GetByIdAsync(int id);
        Task<List<MqttServerDto>> GetListAsync();
        Task CreateAsync(MqttServerDto dto);
        Task UpdateAsync(MqttServerDto dto);
        Task DeleteAsync(int id);
    }
}

