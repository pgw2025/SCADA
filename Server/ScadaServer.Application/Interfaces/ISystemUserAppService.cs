using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface ISystemUserAppService
    {
        Task<LoginResponseDto> LoginAsync(LoginDto loginDto);
        Task<SystemUserDto> GetByIdAsync(int id);
        Task<List<SystemUserDto>> GetListAsync();
        Task CreateAsync(SystemUserDto dto);
        Task UpdateAsync(SystemUserDto dto);
        Task DeleteAsync(int id);
    }
}

