using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 区域应用服务：管理监控区域/分区的增删改查。
    /// </summary>
    public interface IAreaAppService
    {
        /// <summary>按ID查询单个区域；不存在返回 null。</summary>
        Task<AreaDto?> GetByIdAsync(int id);

        /// <summary>查询全部区域。</summary>
        Task<List<AreaDto>> GetListAsync();

        /// <summary>新增一个区域，返回创建后的 DTO（含自增ID）。</summary>
        Task<AreaDto> CreateAsync(AreaDto dto);

        /// <summary>更新一个区域，返回更新后的 DTO。</summary>
        Task<AreaDto> UpdateAsync(AreaDto dto);

        /// <summary>删除一个区域。</summary>
        Task DeleteAsync(int id);
    }
}

