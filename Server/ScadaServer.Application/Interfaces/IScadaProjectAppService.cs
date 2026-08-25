using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    using ScadaServer.Application.DTOs;

    public interface IScadaProjectAppService
    {
        Task<ScadaProjectDto?> GetByIdAsync(int id);
        Task<List<ScadaProjectDto>> GetListAsync();
        /// <summary>创建工程，返回新建的自增Id</summary>
        Task<int> CreateAsync(ScadaProjectDto dto);
        /// <summary>更新工程，返回是否存在并更新成功</summary>
        Task<bool> UpdateAsync(ScadaProjectDto dto);
        Task DeleteAsync(int id);
        /// <summary>获取工程整树（工程 + 页面 + 组件）</summary>
        Task<ScadaProjectFullDto?> GetTreeAsync(int id);
    }
}

