using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 暴露接口（开放 API）应用服务：管理暴露接口配置的增删改查与启停。
    /// 配置变更后通过 <see cref="IExposedApiRegistry"/> 热刷新到网关。
    /// </summary>
    public interface IExposedInterfaceAppService
    {
        /// <summary>按ID查询单个暴露接口；不存在返回 null。</summary>
        Task<ExposedInterfaceDto?> GetByIdAsync(int id);

        /// <summary>查询全部暴露接口。</summary>
        Task<List<ExposedInterfaceDto>> GetListAsync();

        /// <summary>新增一个暴露接口配置。</summary>
        Task CreateAsync(ExposedInterfaceDto dto);

        /// <summary>更新一个暴露接口配置。</summary>
        Task UpdateAsync(ExposedInterfaceDto dto);

        /// <summary>删除一个暴露接口配置。</summary>
        Task DeleteAsync(int id);

        /// <summary>启用/停用暴露接口。</summary>
        Task SetActiveAsync(int id, bool active);
    }
}

