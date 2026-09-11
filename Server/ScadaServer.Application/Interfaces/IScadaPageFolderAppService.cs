using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// SCADA 画面文件夹应用服务（仅 Desktop/Mobile 端）：管理画面列表文件夹的分类、移动、删除与排序。
    /// </summary>
    public interface IScadaPageFolderAppService
    {
        /// <summary>按工程查询全部画面文件夹。</summary>
        Task<List<ScadaPageFolderDto>> GetByProjectAsync(int projectId);

        /// <summary>创建画面文件夹，返回生成的主键。</summary>
        Task<int> CreateAsync(ScadaPageFolderDto dto);

        /// <summary>更新文件夹（重命名/移动父级），成功返回 true，记录不存在返回 false。</summary>
        Task<bool> UpdateAsync(ScadaPageFolderDto dto);

        /// <summary>删除文件夹：mode=reparent（默认，内容上提一级）/ cascade（连同子夹与画面删除）。</summary>
        Task DeleteAsync(int id, string? mode);

        /// <summary>同级统一排序：文件夹段 + 画面段各自全量覆盖。</summary>
        Task ReorderAsync(FolderReorderDto dto);
    }
}