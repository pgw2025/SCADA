using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface ISystemUserAppService
    {
        Task<LoginResponseDto> LoginAsync(LoginDto loginDto);
        Task<SystemUserDto?> GetByIdAsync(int id);
        Task<List<SystemUserDto>> GetListAsync();
        Task CreateAsync(CreateUserDto dto);
        Task UpdateAsync(SystemUserDto dto);
        Task DeleteAsync(int id);

        /// <summary>管理员重置指定用户密码。</summary>
        Task ResetPasswordAsync(int id, string newPassword);

        /// <summary>用户修改自己的密码（需验证原密码）。</summary>
        Task ChangePasswordAsync(int userId, string oldPassword, string newPassword);
    }
}

