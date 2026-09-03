using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 区域应用服务：管理监控区域/分区的增删改查与树形组织。
    /// </summary>
    public interface IAreaAppService
    {
        /// <summary>按ID查询单个区域；不存在返回 null。</summary>
        Task<AreaDto?> GetByIdAsync(int id);

        /// <summary>查询全部区域（平级列表，含树形字段）。</summary>
        Task<List<AreaDto>> GetListAsync();

        /// <summary>查询区域树（根节点列表，含子区域与各节点直接挂载设备数）。</summary>
        Task<List<AreaTreeNodeDto>> GetTreeAsync();

        /// <summary>新增一个区域，返回创建后的 DTO（含自增ID）。</summary>
        Task<AreaDto> CreateAsync(AreaDto dto);

        /// <summary>更新一个区域，返回更新后的 DTO。</summary>
        Task<AreaDto> UpdateAsync(AreaDto dto);

        /// <summary>删除一个区域（存在子区域或设备时拒绝）。</summary>
        Task DeleteAsync(int id);

        /// <summary>
        /// 获取指定区域（含其所有子孙区域）下直接挂载的设备 ID 列表，供"含子区域"过滤设备使用。
        /// </summary>
        Task<List<int>> GetDeviceIdsInSubtreeAsync(int areaId);
    }
}
