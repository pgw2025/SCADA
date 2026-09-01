using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 数据库配置应用服务：管理历史库连接的增删改查。
    /// 注意：主库（MySQL）配置不在本服务管辖，详见 <see cref="IRuntimeDatabaseService"/>。
    /// </summary>
    public interface IDatabaseConfigAppService
    {
        /// <summary>按ID查询单个数据库配置；不存在返回 null。</summary>
        Task<DatabaseConfigDto?> GetByIdAsync(int id);

        /// <summary>查询全部数据库配置。</summary>
        Task<List<DatabaseConfigDto>> GetListAsync();

        /// <summary>新增一个数据库配置。</summary>
        Task CreateAsync(DatabaseConfigDto dto);

        /// <summary>更新一个数据库配置。</summary>
        Task UpdateAsync(DatabaseConfigDto dto);

        /// <summary>删除一个数据库配置。</summary>
        Task DeleteAsync(int id);
    }
}

