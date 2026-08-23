using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IDeviceAppService
    {
        Task<DeviceDto> GetByIdAsync(int id);
        Task<List<DeviceDto>> GetListAsync();
        Task<DeviceDto> CreateAsync(CreateDeviceDto dto);
        Task<DeviceDto> UpdateAsync(DeviceDto dto);
        Task DeleteAsync(int id);
        Task UpdateDeviceConfigTxAsync(int deviceId, string newAddress);
    }
}

