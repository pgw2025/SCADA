using System.Globalization;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.ImportExport;

/// <summary>
/// 标准 CSV 导入解析器。CSV 列与导出模板一致（顺序可变、可选列可缺），
/// 表头行同名匹配。表头为 UTF-8（含 BOM 容错）。
/// </summary>
public class CsvParser
{
    /// <summary>
    /// CSV 标准列名（导出模板同款）。映射到 VariableImportRow 的导入字段。
    /// </summary>
    public static readonly string[] Columns =
    {
        "Key", "Name", "DataType", "Unit", "Min", "Max", "Description", "Address",
        "StoreMode", "StoreIntervalMs", "UpdateMode", "ScaleSlope", "ScaleOffset", "DeadBand", "IsReadOnly"
    };

    public List<VariableImportRow> Parse(Stream fileStream)
    {
        var rows = new List<VariableImportRow>();
        using var reader = new StreamReader(fileStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headerLine = ReadNextDataLine(reader);
        if (headerLine == null)
        {
            rows.Add(FailRow(1, "CSV 文件为空"));
            return rows;
        }

        var header = SplitCsv(headerLine);
        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim().TrimStart('\uFEFF');
            if (name.Length == 0 || colIndex.ContainsKey(name)) continue;
            colIndex[name] = i;
        }

        if (!colIndex.ContainsKey("Key"))
        {
            rows.Add(FailRow(1, "CSV 缺少表头列 Key，请使用导出的模板或含 Key/DataType 的标准表头"));
            return rows;
        }

        var lineNo = 1;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = SplitCsv(line);
            var key = Cell(cells, colIndex, "Key")?.Trim() ?? string.Empty;
            var typeStr = Cell(cells, colIndex, "DataType")?.Trim() ?? string.Empty;
            if (key.Length == 0 && typeStr.Length == 0) continue;

            var row = new VariableImportRow
            {
                RowNumber = lineNo,
                Key = key,
                Name = Cell(cells, colIndex, "Name")?.Trim() ?? key,
                DataTypeRaw = typeStr,
                Address = NullIfEmpty(Cell(cells, colIndex, "Address")),
                Description = NullIfEmpty(Cell(cells, colIndex, "Description"))
            };

            if (row.Key.Length == 0)
                row.SetError("变量标识(Key)为空");
            else if (!System.Text.RegularExpressions.Regex.IsMatch(row.Key, @"^[a-zA-Z0-9_]+$"))
                row.SetError("Key 含非法字符（仅允许字母、数字、下划线）");
            else if (row.Key.Length > 50)
                row.SetError("Key 超过 50 个字符");
            else if (!Enum.TryParse<DataTypeEnum>(typeStr, true, out var dt))
                row.SetError($"无法识别的数据类型 '{typeStr}'");
            else
                row.DataType = dt;

            if (row.HasError)
                row.DataType = DataTypeEnum.BOOL;

            ApplyDetail(row, cells, colIndex);
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// 解析增强字段到行上。可选列解析失败不回填（采用默认值），不以行错误中断这些可选配置。
    /// </summary>
    private static void ApplyDetail(VariableImportRow row, List<string> cells, Dictionary<string, int> colIndex)
    {
        row.Unit = NullIfEmpty(Cell(cells, colIndex, "Unit"));
        row.Min = TryDouble(Cell(cells, colIndex, "Min"));
        row.Max = TryDouble(Cell(cells, colIndex, "Max"));
        row.StoreMode = Enum.TryParse<StoreModeEnum>(Cell(cells, colIndex, "StoreMode"), true, out var sm) ? sm : null;
        row.StoreIntervalMs = TryInt(Cell(cells, colIndex, "StoreIntervalMs"));
        row.UpdateMode = Enum.TryParse<UpdateMode>(Cell(cells, colIndex, "UpdateMode"), true, out var um) ? um : null;
        row.ScaleSlope = TryDouble(Cell(cells, colIndex, "ScaleSlope"));
        row.ScaleOffset = TryDouble(Cell(cells, colIndex, "ScaleOffset"));
        row.DeadBand = TryDouble(Cell(cells, colIndex, "DeadBand"));
        row.IsReadOnly = TryBool(Cell(cells, colIndex, "IsReadOnly"));
    }

    private static VariableImportRow FailRow(int rowNo, string reason) =>
        new() { RowNumber = rowNo, HasError = true, ErrorReason = reason, DataType = DataTypeEnum.BOOL };

    private static string? ReadNextDataLine(StreamReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }
        return null;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }

    private static string? Cell(List<string> cells, Dictionary<string, int> colIndex, string name) =>
        colIndex.TryGetValue(name, out var i) && i < cells.Count ? cells[i] : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static double? TryDouble(string? s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static int? TryInt(string? s) => int.TryParse(s, out var v) ? v : null;
    private static bool? TryBool(string? s) => bool.TryParse(s, out var v) ? v : null;
}