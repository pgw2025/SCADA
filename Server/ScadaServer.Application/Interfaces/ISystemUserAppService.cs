using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 系统用户应用服务：管理用户登录、增删改查及密码重置/修改。
    /// </summary>
    public interface ISystemUserAppService
    {
        /// <summary>用户登录，成功后返回包含 Token 的登录响应。</summary>
        Task<LoginResponseDto> LoginAsync(LoginDto loginDto);

        /// <summary>按ID查询单个用户；不存在返回 null。</summary>
        Task<SystemUserDto?> GetByIdAsync(int id);

        /// <summary>查询全部用户。</summary>
        Task<List<SystemUserDto>> GetListAsync();

        /// <summary>新增一个用户。</summary>
        Task CreateAsync(CreateUserDto dto);

        /// <summary>更新一个用户。</summary>
        Task UpdateAsync(SystemUserDto dto);

        /// <summary>删除一个用户。</summary>
        Task DeleteAsync(int id);

        /// <summary>管理员重置指定用户密码。</summary>
        Task ResetPasswordAsync(int id, string newPassword);

        /// <summary>用户修改自己的密码（需验证原密码）。</summary>
        Task ChangePasswordAsync(int userId, string oldPassword, string newPassword);
    }
}

