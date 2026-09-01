using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 配置变更日志应用服务：管理配置变更日志记录的增删改查。
    /// </summary>
    public interface IConfigLogAppService
    {
        /// <summary>按ID查询单条配置日志；不存在返回 null。</summary>
        Task<ConfigLogDto?> GetByIdAsync(int id);

        /// <summary>查询全部配置日志。</summary>
        Task<List<ConfigLogDto>> GetListAsync();

        /// <summary>新增一条配置日志。</summary>
        Task CreateAsync(ConfigLogDto dto);

        /// <summary>更新一条配置日志。</summary>
        Task UpdateAsync(ConfigLogDto dto);

        /// <summary>删除一条配置日志。</summary>
        Task DeleteAsync(int id);
    }
}

