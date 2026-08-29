using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.ImportExport;

/// <summary>
/// 按文件扩展名分发到的导入解析器（无状态，可注册为单例）。
/// </summary>
public class VariableImportParser : IVariableImportParser
{
    private readonly TiaXlsxParser _xlsx = new();
    private readonly CsvParser _csv = new();

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