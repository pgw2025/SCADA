using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 组态组件应用服务：管理画面内组件的增删改查。
    /// </summary>
    public interface IHmiComponentAppService
    {
        /// <summary>按ID查询单个组件；不存在返回 null。</summary>
        Task<HmiComponentDto?> GetByIdAsync(int id);

        /// <summary>查询全部组件。</summary>
        Task<List<HmiComponentDto>> GetListAsync();

        /// <summary>创建组件，返回新建的自增Id</summary>
        Task<int> CreateAsync(HmiComponentDto dto);

        /// <summary>更新组件，返回是否存在并更新成功</summary>
        Task<bool> UpdateAsync(HmiComponentDto dto);

        /// <summary>删除组件。</summary>
        Task DeleteAsync(int id);
    }
}

