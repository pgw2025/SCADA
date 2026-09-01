using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.ImportExport;

/// <summary>
/// 按文件扩展名分发到的导入解析器（无状态，可注册为单例）。
/// </summary>
public class VariableImportParser : IVariableImportParser
{
    // 具体格式解析器均无状态，故可作为字段复用，不需要每次重建
    private readonly TiaXlsxParser _xlsx = new();
    private readonly CsvParser _csv = new();

    /// <summary>
    /// 依据文件扩展名将解析分发给对应的格式解析器。
    /// .xlsx/.xls → TIA 解析器；.csv 或无扩展名 → CSV 解析器；
    /// 其余未知格式直接返回一条"不支持的文件格式"错误行。
    /// </summary>
    /// <param name="fileStream">文件内容流</param>
    /// <param name="fileName">原始文件名（用于判断扩展名以选择解析器）</param>
    /// <returns>解析得到的导入行列表。</returns>
    public Task<List<VariableImportRow>> ParseAsync(Stream fileStream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        List<VariableImportRow> rows = ext switch
        {
            ".xlsx" or ".xls" => _xlsx.Parse(fileStream),
            ".csv" or "" => _csv.Parse(fileStream),
            _ => new List<VariableImportRow>
            {
                new() { RowNumber = 1, HasError = true, ErrorReason = "不支持的文件格式（仅支持 .xlsx 与 .csv）", DataType = DataTypeEnum.BOOL }
            }
        };
        return Task.FromResult(rows);
    }
}