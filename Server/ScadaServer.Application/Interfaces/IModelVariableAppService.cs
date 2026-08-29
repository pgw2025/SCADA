using ScadaServer.Domain.Entities;
using ScadaServer.Application.DTOs;
namespace ScadaServer.Application.Interfaces
{
    public interface IModelVariableAppService
    {
        Task<ModelVariableDto?> GetByIdAsync(int id);
        Task<List<ModelVariableDto>> GetListAsync();
        Task<List<ModelVariableDto>> GetByModelIdAsync(int modelId);
        Task<ModelVariableDto> CreateAsync(ModelVariableDto dto);
        Task<ModelVariableDto> UpdateAsync(ModelVariableDto dto);
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

