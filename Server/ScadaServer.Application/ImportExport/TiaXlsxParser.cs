using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.ImportExport;

/// <summary>
/// TIA Portal 变量表 xlsx 解析：把 TIA 导出的 PLC 变量表映射为系统可导入的变量行。
/// <para>
/// TIA 导出文件特征：
///  - 前若干行为元信息（项目名、导出时间、版本等），表头行需扫描定位；
///  - 表头列名随语言环境不同（名称/Name、数据类型/Data Type、逻辑地址/Logical Address、注释/Comment、路径/Path）；
///  - 数据类型可能带长度（String[20]）、数组/结构体等无法直接映射的类型。
/// 本解析器：动态定位表头、中英文列名兼容、TIA 类型→系统类型映射，无法处理的类型记为行错误不中断整批。
/// </para>
/// </summary>
public class TiaXlsxParser
{
    /// <summary>
    /// 表头扫描范围：TIA 导出文件的元信息行数一般有限，扫前 N 行足够。
    /// </summary>
    private const int HeaderScanMaxRows = 10;

    /// <summary>
    /// 列名匹配（键为规范化小写；含中英文常见 TIA 列名）。
    /// </summary>
    private static readonly Dictionary<string, string> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "name_zh", ["名称"] = "name_zh",
        ["datatype"] = "datatype_zh", ["数据类型"] = "datatype_zh", ["data type"] = "datatype_zh",
        ["logicaladdress"] = "addr", ["逻辑地址"] = "addr",
        ["comment"] = "comment", ["注释"] = "comment",
        ["path"] = "path", ["路径"] = "path"
    };

    /// <summary>
    /// 解析 TIA Portal 导出的变量表 xlsx 流，将其转换为可变导入行列表。
    /// 处理策略：动态定位表头 → 建立规范列名到列号的映射 → 逐行读取并校验。
    /// </summary>
    /// <param name="fileStream">TIA xlsx 文件流（由调用方负责读取与释放）</param>
    /// <returns>导入行列表；无法识别表头时返回一条整体错误行，不会抛出异常。</returns>
    public List<VariableImportRow> Parse(Stream fileStream)
    {
        var rows = new List<VariableImportRow>();
        using var workbook = new ClosedXML.Excel.XLWorkbook(fileStream);
        // TIA 导出的变量表通常位于首个工作表，故直接取第一张表
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null) return rows;

        var (headerRowIndex, colMap) = LocateHeader(worksheet);
        if (headerRowIndex <= 0)
        {
            // 无法定位表头：返回一条整体错误，前端可据此提示"不是有效的 TIA 变量表"
            rows.Add(new VariableImportRow
            {
                RowNumber = 1,
                HasError = true,
                ErrorReason = "未能识别表头（缺少名称/数据类型列），请确认文件为 TIA 变量表导出"
            });
            return rows;
        }

        // 将规范列名解析为实际 Excel 列号；缺列时返回 -1，后续读取逻辑据此跳过该列
        var colName = Axis(colMap, "name_zh");
        var colType = Axis(colMap, "datatype_zh");
        var colAddr = Axis(colMap, "addr");
        var colComment = Axis(colMap, "comment");
        var colPath = Axis(colMap, "path");

        // 表头行以下即数据区，扫到最后一个已使用的行作为数据下界
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRowIndex;
        for (var r = headerRowIndex + 1; r <= lastRow; r++)
        {
            var key = GetCellText(worksheet, r, colName);
            var typeRaw = GetCellText(worksheet, r, colType);

            // 空行（无名称且无类型）跳过
            if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(typeRaw))
                continue;

            var row = new VariableImportRow
            {
                RowNumber = r,
                Key = key,
                Name = string.IsNullOrWhiteSpace(key) ? string.Empty : key,
                DataTypeRaw = typeRaw,
                Address = SafeString(GetCellText(worksheet, r, colAddr)),
                Description = SafeString(GetCellText(worksheet, r, colComment)),
                Path = SafeString(GetCellText(worksheet, r, colPath))
            };

            // Key 校验：与后端 DataPointDto 规则一致
            if (string.IsNullOrWhiteSpace(row.Key))
            {
                row.HasError = true;
                row.ErrorReason = "变量名称为空";
            }
            else if (!System.Text.RegularExpressions.Regex.IsMatch(row.Key, @"^[a-zA-Z0-9_]+$"))
            {
                row.HasError = true;
                row.ErrorReason = "名称含非法字符（仅允许字母、数字、下划线）";
            }
            else if (row.Key.Length > 50)
            {
                row.HasError = true;
                row.ErrorReason = "名称超过 50 个字符";
            }

            // 类型映射（仅在无既有错误时才解析类型，类型无法识别标记为行错误）
            if (!row.HasError)
            {
                var mapResult = TiaTypeMapping.TryMap(typeRaw);
                if (mapResult.Success)
                {
                    row.DataType = mapResult.DataType;
                    row.IsApproxType = mapResult.IsApprox;
                }
                else
                {
                    row.HasError = true;
                    row.ErrorReason = $"无法识别数据类型 '{typeRaw}'";
                }
            }
            else
            {
                row.DataType = DataTypeEnum.BOOL; // 占位，避免前端渲染空值；HasError 为 true 不会导入
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// 定位表头行并建立"规范列名 → Excel 列号"映射。
    /// </summary>
    private static (int HeaderRow, Dictionary<string, int> ColMap) LocateHeader(ClosedXML.Excel.IXLWorksheet ws)
    {
        var scanRows = Math.Min(HeaderScanMaxRows, ws.LastRowUsed()?.RowNumber() ?? 0);
        for (var r = 1; r <= scanRows; r++)
        {
            var colMap = new Dictionary<string, int>();
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (var c = 1; c <= lastCol; c++)
            {
                var cell = ws.Cell(r, c).GetString().Trim();
                if (cell.Length == 0) continue;
                if (!ColumnAliases.TryGetValue(cell, out var canonical)) continue;
                // 同一规范列出现多次时以首次为准
                if (!colMap.ContainsKey(canonical)) colMap[canonical] = c;
            }

            // 表头合法条件：同时具备名称与数据类型两列
            if (colMap.ContainsKey("name_zh") && colMap.ContainsKey("datatype_zh"))
                return (r, colMap);
        }
        return (0, new Dictionary<string, int>());
    }

    /// <summary>
    /// 从列映射中按规范列名取列号；映射中不存在（该列缺失）时返回 -1。
    /// 返回 -1 是为了让上游读取逻辑统一判断"列为空"，避免逐处判空。
    /// </summary>
    private static int Axis(Dictionary<string, int> colMap, string canonical) =>
        colMap.TryGetValue(canonical, out var c) ? c : -1;

    /// <summary>
    /// 读取指定单元格文本并去除首尾空白；列号小于等于 0（缺列）时返回空串。
    /// </summary>
    private static string GetCellText(ClosedXML.Excel.IXLWorksheet ws, int row, int col) =>
        col <= 0 ? string.Empty : ws.Cell(row, col).GetString().Trim();

    /// <summary>
    /// 将空白字符串归一化为 null（便于前端区分"有值"与"未填"）。
    /// </summary>
    private static string? SafeString(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>
/// TIA 数据类型 → 系统 DataTypeEnum 映射规则。
/// </summary>
public static class TiaTypeMapping
{
    /// <summary>
    /// TIA 基本类型 → 系统类型精确映射表。
    /// 键为规范化小写（不区分大小写）；值为目标类型以及"是否为近似映射"标志
    /// （近似映射如 Time→DINT，精度/语义并非一一对应）。不在表内的类型视为无法识别。
    /// </summary>
    private static readonly Dictionary<string, (DataTypeEnum Type, bool IsApprox)> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bool"] = (DataTypeEnum.BOOL, false),
        ["int"] = (DataTypeEnum.INT, false),
        ["dint"] = (DataTypeEnum.DINT, false),
        ["word"] = (DataTypeEnum.WORD, false),
        ["dword"] = (DataTypeEnum.UINT32, false),
        ["real"] = (DataTypeEnum.REAL, false),
        ["lreal"] = (DataTypeEnum.DOUBLE, false),
        ["char"] = (DataTypeEnum.CHAR, false),
        ["byte"] = (DataTypeEnum.BYTE, false),
        ["uint"] = (DataTypeEnum.UINT16, false),
        ["time"] = (DataTypeEnum.DINT, true)     // 近似：Time(ms) → 32位整数
    };

    /// <summary>
    /// 尝试将 TIA 数据类型字符串映射为系统 DataTypeEnum。
    /// 按"先处理前缀类特例（String、Array）→ 再查精确表"的顺序判断；
    /// 空值、未知类型或明确不支持的复杂类型返回失败，由调用方记为该行错误。
    /// </summary>
    /// <param name="typeRaw">TIA 类型原文，允许为 null/空白（视为失败）</param>
    /// <returns>映射结果；<see cref="MapResult.Success"/> 表示映射成功。</returns>
    public static MapResult TryMap(string typeRaw)
    {
        var t = (typeRaw ?? string.Empty).Trim();
        if (t.Length == 0) return MapResult.Fail;

        // String / String[n]（如 String[20]）均映射为 STRING；用前缀匹配以兼容带长度写法
        if (t.StartsWith("String", StringComparison.OrdinalIgnoreCase))
            return MapResult.Ok(DataTypeEnum.STRING, false);

        // Array[...] of X 由前缀显式判定为不支持的复杂类型 → 失败；
        // 结构体、LInt/ULInt/LWord/LReal 等因不在精确表中，亦会自然落到下方失败分支
        if (t.StartsWith("Array", StringComparison.OrdinalIgnoreCase))
            return MapResult.Fail;

        return Exact.TryGetValue(t, out var hit)
            ? MapResult.Ok(hit.Type, hit.IsApprox)
            : MapResult.Fail;
    }

    public struct MapResult
    {
        public bool Success;
        public DataTypeEnum DataType;
        public bool IsApprox;

        public static MapResult Fail => new() { Success = false };
        public static MapResult Ok(DataTypeEnum type, bool approx) =>
            new() { Success = true, DataType = type, IsApprox = approx };
    }
}