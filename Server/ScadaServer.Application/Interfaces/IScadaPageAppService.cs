using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// SCADA 画面应用服务：管理组态画面的增删改查及按工程/端筛选。
    /// </summary>
    public interface IScadaPageAppService
    {
        /// <summary>按ID查询单个画面；不存在返回 null。</summary>
        Task<ScadaPageDto?> GetByIdAsync(int id);

        /// <summary>查询全部画面。</summary>
        Task<List<ScadaPageDto>> GetListAsync();

        /// <summary>按项目过滤页面列表，projectId 为 null 时返回全部；platform 可进一步按端过滤（Desktop/Mobile）</summary>
        Task<List<ScadaPageDto>> GetByProjectAsync(int? projectId, string? platform = null);

        /// <summary>创建页面，返回新建的自增Id</summary>
        Task<int> CreateAsync(ScadaPageDto dto);

        /// <summary>更新页面，返回是否存在并更新成功</summary>
        Task<bool> UpdateAsync(ScadaPageDto dto);

        /// <summary>删除页面。</summary>
        Task DeleteAsync(int id);
    }
}

