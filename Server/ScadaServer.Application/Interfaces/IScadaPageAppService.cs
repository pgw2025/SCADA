using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IScadaPageAppService
    {
        Task<ScadaPageDto?> GetByIdAsync(int id);
        Task<List<ScadaPageDto>> GetListAsync();
        /// <summary>按项目过滤页面列表，projectId 为 null 时返回全部；platform 可进一步按端过滤（Desktop/Mobile）</summary>
        Task<List<ScadaPageDto>> GetByProjectAsync(int? projectId, string? platform = null);
        /// <summary>创建页面，返回新建的自增Id</summary>
        Task<int> CreateAsync(ScadaPageDto dto);
        /// <summary>更新页面，返回是否存在并更新成功</summary>
        Task<bool> UpdateAsync(ScadaPageDto dto);
        Task DeleteAsync(int id);
    }
}

