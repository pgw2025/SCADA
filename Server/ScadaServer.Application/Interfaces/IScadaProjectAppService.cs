using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    using ScadaServer.Application.DTOs;

    /// <summary>
    /// SCADA 工程应用服务：管理工程的增删改查、整树加载以及工程的迁移包导入/导出。
    /// </summary>
    public interface IScadaProjectAppService
    {
        /// <summary>按ID查询单个工程；不存在返回 null。</summary>
        Task<ScadaProjectDto?> GetByIdAsync(int id);

        /// <summary>查询全部工程。</summary>
        Task<List<ScadaProjectDto>> GetListAsync();

        /// <summary>创建工程，返回新建的自增Id</summary>
        Task<int> CreateAsync(ScadaProjectDto dto);

        /// <summary>更新工程，返回是否存在并更新成功</summary>
        Task<bool> UpdateAsync(ScadaProjectDto dto);

        /// <summary>删除工程。</summary>
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

        /// <summary>获取工程已授权用户列表（联查用户名）；工程不存在返回 null。</summary>
        Task<List<ScadaProjectAuthorizedUserDto>?> GetAuthorizedUsersAsync(int projectId);

        /// <summary>全量覆盖工程授权用户集合；工程不存在返回 false，含不存在用户抛 ArgumentException。</summary>
        Task<bool> SaveAuthorizationsAsync(int projectId, List<int> userIds);
    }
}

