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
        "StoreMode", "StoreIntervalMs", "UpdateMode", "ScaleSlope", "ScaleOffset", "DeadBand", "IsReadOnly"
    };

    public byte[] ExportXlsx(List<ModelVariableDto> variables)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Variables");

        // 表头
        for (var c = 0; c < Headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = Headers[c];
        }
        ws.Row(1).Style.Font.Bold = true;

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
            ws.Cell(row, 12).Value = v.ScaleSlope;
            ws.Cell(row, 13).Value = v.ScaleOffset;
            ws.Cell(row, 14).Value = v.DeadBand;
            ws.Cell(row, 15).Value = v.IsReadOnly;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportCsv(List<ModelVariableDto> variables)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Headers)).Append('\n');

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
              .Append(Csv.Number(v.ScaleSlope)).Append(',')
              .Append(Csv.Number(v.ScaleOffset)).Append(',')
              .Append(Csv.Number(v.DeadBand)).Append(',')
              .Append(v.IsReadOnly ? "true" : "false")
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
        public static string Quote(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            var escaped = s.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        public static string Number(double? v) =>
            v == null ? "" : v.Value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}