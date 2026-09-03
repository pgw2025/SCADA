using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.ImportExport;

/// <summary>
/// 模型变量导出服务：生成 Excel(xlsx) 与 CSV 两种格式。
/// 导出列与 <see cref="CsvParser.Columns"/> 模板一致，文件可直接再导入（往返一致）。
/// </summary>
public class VariableExportService
{
    /// <summary>
    /// 导出表头（与导入模板同款列名）。
    /// </summary>
    private static readonly string[] Headers =
    {
        "Key", "Name", "DataType", "Unit", "Min", "Max", "Description", "Address",
        "StoreMode", "StoreIntervalMs", "UpdateMode", "ScaleExpression", "DeadBand",
        "IsReadOnly", "AccessMode", "IsRequired", "Sort", "IsEnabled"
    };

    /// <summary>
    /// 将变量列表导出为 Excel(xlsx) 字节数组。首行固定为表头，数据自第 2 行起；
    /// 各列按 <see cref="Headers"/> 顺序写出，列号与表头数组下标一一对应（列号 = 下标 + 1）。
    /// </summary>
    /// <param name="variables">待导出的模型变量列表</param>
    /// <returns>xlsx 文件的字节数组</returns>
    public byte[] ExportXlsx(List<ModelVariableDto> variables)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Variables");

        // 表头：从下标 0 起，写到第 1 行的第 c+1 列
        for (var c = 0; c < Headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = Headers[c];
        }
        ws.Row(1).Style.Font.Bold = true;   // 加粗表头增强可读性

        // 数据行：第 r 条变量写到第 r+1 行（第 1 行已被表头占用），列按固定顺序映射
        for (var r = 0; r < variables.Count; r++)
        {
            var v = variables[r];
            var row = r + 2;
            ws.Cell(row, 1).Value = v.Key;
            ws.Cell(row, 2).Value = v.Name;
            ws.Cell(row, 3).Value = v.DataType.ToString();
            ws.Cell(row, 4).Value = v.Unit ?? string.Empty;
            ws.Cell(row, 5).Value = v.Min;
            ws.Cell(row, 6).Value = v.Max;
            ws.Cell(row, 7).Value = v.Description ?? string.Empty;
            ws.Cell(row, 8).Value = ExtractAddress(v.ExtensionData);
            ws.Cell(row, 9).Value = v.StoreMode.ToString();
            ws.Cell(row, 10).Value = v.StoreIntervalMs;
            ws.Cell(row, 11).Value = v.UpdateMode.ToString();
            ws.Cell(row, 12).Value = v.ScaleExpression ?? string.Empty;
            ws.Cell(row, 13).Value = v.DeadBand;
            // 权限列：IsReadOnly 兼容列与 AccessMode 权威列并存输出，保证往返无损
            ws.Cell(row, 14).Value = v.IsReadOnly;
            ws.Cell(row, 15).Value = v.AccessMode ?? "Read";
            ws.Cell(row, 16).Value = v.IsRequired;
            ws.Cell(row, 17).Value = v.Sort;
            ws.Cell(row, 18).Value = v.IsEnabled;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 将变量列表导出为 CSV 字节数组（UTF-8 带 BOM）。
    /// 字段统一以双引号包裹并做引号转义，数值列按固定小数点格式写出；
    /// 表头与每行列序与 <see cref="Headers"/> 一致，保证可直接被 <see cref="CsvParser"/> 回读。
    /// </summary>
    /// <param name="variables">待导出的模型变量列表</param>
    /// <returns>CSV 文件的字节数组</returns>
    public byte[] ExportCsv(List<ModelVariableDto> variables)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Headers)).Append('\n');

        // 交替写入"引号包裹的文本列/数值列/布尔列"+ 逗号，构造每行 CSV 记录
        foreach (var v in variables)
        {
            sb.Append(Csv.Quote(v.Key)).Append(',')
              .Append(Csv.Quote(v.Name)).Append(',')
              .Append(Csv.Quote(v.DataType.ToString())).Append(',')
              .Append(Csv.Quote(v.Unit)).Append(',')
              .Append(Csv.Number(v.Min)).Append(',')
              .Append(Csv.Number(v.Max)).Append(',')
              .Append(Csv.Quote(v.Description)).Append(',')
              .Append(Csv.Quote(ExtractAddress(v.ExtensionData) ?? string.Empty)).Append(',')
              .Append(Csv.Quote(v.StoreMode.ToString())).Append(',')
              .Append(v.StoreIntervalMs).Append(',')
              .Append(Csv.Quote(v.UpdateMode.ToString())).Append(',')
              .Append(Csv.Quote(v.ScaleExpression)).Append(',')
              .Append(Csv.Number(v.DeadBand)).Append(',')
              .Append(v.IsReadOnly ? "true" : "false").Append(',')
              .Append(Csv.Quote(v.AccessMode ?? "Read")).Append(',')
              .Append(v.IsRequired ? "true" : "false").Append(',')
              .Append(v.Sort).Append(',')
              .Append(v.IsEnabled ? "true" : "false")
              .Append('\n');
        }

        // UTF-8 带 BOM：Excel 直接打开不乱码
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var result = new byte[bom.Length + bytes.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(bytes, 0, result, bom.Length, bytes.Length);
        return result;
    }

    /// <summary>
    /// 从扩展数据中取回 TIA 导入时保存的逻辑地址。
    /// </summary>
    private static string? ExtractAddress(Dictionary<string, string>? ext)
    {
        if (ext == null || !ext.TryGetValue("address", out var addr)) return null;
        return string.IsNullOrWhiteSpace(addr) ? null : addr;
    }

    private static class Csv
    {
        /// <summary>
        /// 将字符串字段安全包裹为 CSV 引号字段：用 `""` 转义字段内的引号，避免破坏列结构。
        /// 空值输出为空的双引号对 `""`（表示空列），保证列数量稳定。
        /// </summary>
        public static string Quote(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            var escaped = s.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        /// <summary>
        /// 将可空小数格式化为 CSV 数值文本：
        /// 使用固定小数点格式（0.########，最多 8 位小数）并以 InvariantCulture 输出，
        /// 确保不随系统区域/小数点符号变化，导出的数字可被稳定回读。null 输出空串。
        /// </summary>
        public static string Number(double? v) =>
            v == null ? "" : v.Value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}