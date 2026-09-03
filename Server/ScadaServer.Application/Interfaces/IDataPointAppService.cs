using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 模型变量应用服务：管理数据模型变量模板的增删改查及批量导入/导出。
    /// 模型变量（DataPoint）是变量模板，与设备实例变量（DataPointMapping）相互独立。
    /// </summary>
    public interface IDataPointAppService
    {
        /// <summary>按ID查询单个模型变量；不存在返回 null。</summary>
        Task<DataPointDto?> GetByIdAsync(int id);

        /// <summary>查询全部模型变量。</summary>
        Task<List<DataPointDto>> GetListAsync();

        /// <summary>按模型ID查询该模型下的全部变量。</summary>
        Task<List<DataPointDto>> GetByModelIdAsync(int modelId);

        /// <summary>新增一个模型变量，返回创建后的 DTO。</summary>
        Task<DataPointDto> CreateAsync(DataPointDto dto);

        /// <summary>更新一个模型变量，返回更新后的 DTO。</summary>
        Task<DataPointDto> UpdateAsync(DataPointDto dto);

        /// <summary>删除一个模型变量。</summary>
        Task DeleteAsync(int id);

        /// <summary>
        /// 解析导入文件并做模型内 Key 冲突比对，返回预览结果（不入库）。
        /// </summary>
        Task<VariableImportPreviewDto> ImportPreviewAsync(int modelId, Stream fileStream, string fileName);

        /// <summary>
        /// 确认导入：按冲突策略批量写入（单事务）。
        /// </summary>
        Task<VariableImportResultDto> ImportAsync(int modelId, Stream fileStream, string fileName, ConflictStrategy strategy);

        /// <summary>
        /// 导出模型变量。format 支持 "xlsx" 或 "csv"。
        /// </summary>
        Task<byte[]> ExportAsync(int modelId, string format);
    }
}

