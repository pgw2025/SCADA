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
        /// <summary>导出工程为迁移包（工程+画面+组件）；工程不存在返回 null</summary>
        Task<ScadaTransferPackageDto?> ExportAsync(int id);
        /// <summary>导入工程迁移包（事务整体创建）；格式非法抛 ArgumentException</summary>
        Task<ScadaImportResultDto> ImportAsync(ScadaTransferPackageDto package);
        /// <summary>导出单个画面为迁移包；画面不存在返回 null</summary>
        Task<ScadaTransferPackageDto?> ExportPageAsync(int pageId);
        /// <summary>导入画面迁移包到指定工程；工程不存在/格式非法抛 ArgumentException</summary>
        Task<ScadaImportResultDto> ImportPageAsync(int projectId, ScadaTransferPackageDto package);
    }
}

