using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// HMI 组件模板应用服务：组件库模板元数据的增删改查与导入导出（组件库动态化）。
    /// </summary>
    public interface IHmiWidgetTemplateAppService
    {
        /// <summary>查询全部模板（按 SortOrder 升序、同序按 Id）。</summary>
        Task<List<HmiWidgetTemplateDto>> GetListAsync();

        /// <summary>按主键查询模板；不存在返回 null。</summary>
        Task<HmiWidgetTemplateDto?> GetByIdAsync(int id);

        /// <summary>创建模板（校验后写入），返回生成的主键。</summary>
        Task<int> CreateAsync(HmiWidgetTemplateDto dto);

        /// <summary>更新模板（校验后全量覆盖），成功返回 true，不存在返回 false。</summary>
        Task<bool> UpdateAsync(HmiWidgetTemplateDto dto);

        /// <summary>删除模板；系统内置模板（IsSystem）拒绝删除。</summary>
        Task DeleteAsync(int id);

        /// <summary>导入模板（单条）：键冲突时按 ConflictMode 覆盖或改名另存。</summary>
        Task<ImportResult> ImportAsync(WidgetTemplateImportDto import);

        /// <summary>导出模板（单条）。</summary>
        Task<WidgetTemplateExportDto> ExportAsync(int id);

        /// <summary>批量导出模板（多模板打一个文件）。</summary>
        Task<WidgetTemplateBundleDto> ExportBundleAsync(IEnumerable<int> ids);
    }
}
