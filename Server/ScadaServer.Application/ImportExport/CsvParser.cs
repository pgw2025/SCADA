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
        "StoreMode", "StoreIntervalMs", "UpdateMode", "ScaleExpression", "DeadBand", "IsReadOnly"
    };

    /// <summary>
    /// 解析标准 CSV 流为导入行列表。
    /// 处理要点：按表头列名（不区分大小写）动态匹配列位置、支持引号包裹与转义、
    /// 空行与"无 Key 且无类型"整行跳过，逐行校验 Key/DataType 合法性。
    /// </summary>
    /// <param name="fileStream">CSV 文件流（UTF-8，可带/不带 BOM，由调用方负责释放）</param>
    /// <returns>导入行列表；文件为空或缺少表头 Key 列时返回单条整体错误行。</returns>
    public List<VariableImportRow> Parse(Stream fileStream)
    {
        var rows = new List<VariableImportRow>();
        // detectEncodingFromByteOrderMarks 可在 UTF-8 带 BOM 时自动识别编码，保证中文不乱码
        using var reader = new StreamReader(fileStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headerLine = ReadNextDataLine(reader);
        if (headerLine == null)
        {
            rows.Add(FailRow(1, "CSV 文件为空"));
            return rows;
        }

        var header = SplitCsv(headerLine);
        // 按表头名建立"列名 → 列序号"映射（不区分大小写）；同名列只取首个
        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            // 首列名可能残留 UTF-8 BOM 字符（\uFEFF），必须剥除才能匹配英文 Key 等列名
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

            // 逐行拆分并按表头列名取值；Name 缺省时退化为 Key（兼容只含 Key 的导入）
            var cells = SplitCsv(line);
            var key = Cell(cells, colIndex, "Key")?.Trim() ?? string.Empty;
            var typeStr = Cell(cells, colIndex, "DataType")?.Trim() ?? string.Empty;
            if (key.Length == 0 && typeStr.Length == 0) continue;   // 整行无实质内容则跳过

            var row = new VariableImportRow
            {
                RowNumber = lineNo,
                Key = key,
                Name = Cell(cells, colIndex, "Name")?.Trim() ?? key,
                DataTypeRaw = typeStr,
                Address = NullIfEmpty(Cell(cells, colIndex, "Address")),
                Description = NullIfEmpty(Cell(cells, colIndex, "Description"))
            };

            // 依次校验 Key 是否为空/含非法字符/超长，以及 DataType 是否可识别为系统枚举；
            // 任一失败通过 SetError 标记 HasError，但不中断整批解析
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

            // 出错行补默认类型，避免前端渲染空值；HasError 为 true 时不会进入导入
            if (row.HasError)
                row.DataType = DataTypeEnum.BOOL;

            ApplyDetail(row, cells, colIndex);   // 其余可选增强字段按最佳努力填充
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
        row.ScaleExpression = NullIfEmpty(Cell(cells, colIndex, "ScaleExpression"));
        row.DeadBand = TryDouble(Cell(cells, colIndex, "DeadBand"));
        row.IsReadOnly = TryBool(Cell(cells, colIndex, "IsReadOnly"));
    }

    /// <summary>
    /// 构造一条"整文件级"错误行（用于文件为空或表头无效等场景），
    /// 类型取默认值 BOOL 以避免前端空值，但因 HasError 为 true 不会进入导入。
    /// </summary>
    private static VariableImportRow FailRow(int rowNo, string reason) =>
        new() { RowNumber = rowNo, HasError = true, ErrorReason = reason, DataType = DataTypeEnum.BOOL };

    /// <summary>
    /// 读取下一条数据行：跳过开头可能出现的空行（如表头前留白），返回首个非空行；无数据时返回 null。
    /// </summary>
    private static string? ReadNextDataLine(StreamReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }
        return null;
    }

    /// <summary>
    /// 按 CSV 标准（RFC 兼容的子集）拆分一行文本为单元格列表：
    /// 支持用双引号包裹含逗号的字段，且使用相邻两个双引号 `""` 转义字段内的引号。
    /// 该解析面向整行（不跨行），因此不做跨行的多行字段处理。
    /// </summary>
    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;          // 当前是否处于引号包裹状态（此时逗号不作为分隔符）
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                // 引号内的成对引号（""）表示一个转义的字面引号，追加后再跳过下一个字符
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;   // 单个引号切换包裹状态
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

    /// <summary>
    /// 按列名从当前行单元格中取值；该列缺失或越界（列数少于表头）时返回 null。
    /// </summary>
    private static string? Cell(List<string> cells, Dictionary<string, int> colIndex, string name) =>
        colIndex.TryGetValue(name, out var i) && i < cells.Count ? cells[i] : null;

    /// <summary>空白文本归一化为 null。</summary>
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    /// <summary>按不随区域变化(fixed)的格式解析小数；解析失败返回 null（保持默认值）。</summary>
    private static double? TryDouble(string? s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    /// <summary>解析整数；失败返回 null。</summary>
    private static int? TryInt(string? s) => int.TryParse(s, out var v) ? v : null;
    /// <summary>解析布尔；失败返回 null。</summary>
    private static bool? TryBool(string? s) => bool.TryParse(s, out var v) ? v : null;
}