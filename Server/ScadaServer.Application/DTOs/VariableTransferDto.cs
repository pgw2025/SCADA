using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs;

/// <summary>
/// 冲突处理策略（导入时遇到模型内 Key 已存在的处理方式）
/// </summary>
public enum ConflictStrategy
{
    /// <summary>
    /// 跳过冲突行（默认）
    /// </summary>
    Skip,

    /// <summary>
    /// 覆盖更新已有变量（仅更新文件出现的字段，未列出字段保持原值）
    /// </summary>
    Overwrite,

    /// <summary>
    /// 只要存在任一冲突即整体失败
    /// </summary>
    Abort
}

/// <summary>
/// 单行导入解析结果（预览展示用）
/// </summary>
public class VariableImportRow
{
    /// <summary>
    /// 文件内行号（用于排错定位，从 1 开始）
    /// </summary>
    public int RowNumber { get; set; }

    /// <summary>
    /// 变量标识（来自 TIA Name 列 / CSV Key 列）
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 变量名称（TIA 无独立显示名，与 Key 相同；CSV 取 Name 列）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 原始数据类型字符串（如 "Int"、"String[20]"、"Bool"），TIA 解析特有
    /// </summary>
    public string? DataTypeRaw { get; set; }

    /// <summary>
    /// 映射后的系统数据类型
    /// </summary>
    public DataTypeEnum DataType { get; set; }

    /// <summary>
    /// 是否就近映射的近似类型（SInt/USInt 等，无精确对应）
    /// </summary>
    public bool IsApproxType { get; set; }

    /// <summary>
    /// 逻辑地址（TIA 解析特有，如 %I0.0），导入时写入 ExtensionData["address"]
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// 注释 → 变量描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 变量表路径（TIA 解析特有，可选展示）
    /// </summary>
    public string? Path { get; set; }

    // --- 增强字段（CSV 模板提供；TIA 导入通常不提供，采用默认值）---
    public string? Unit { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public StoreModeEnum? StoreMode { get; set; }
    public int? StoreIntervalMs { get; set; }
    public UpdateMode? UpdateMode { get; set; }
    public double? ScaleSlope { get; set; }
    public double? ScaleOffset { get; set; }
    public double? DeadBand { get; set; }
    public bool? IsReadOnly { get; set; }

    /// <summary>
    /// 该行是否为非法/无法处理的行（类型无法识别、Key 非法、为空白等）
    /// </summary>
    public bool HasError { get; set; }

    /// <summary>
    /// 错误原因（HasError 为 true 时的说明）
    /// </summary>
    public string? ErrorReason { get; set; }

    /// <summary>
    /// 标记该行为错误行并附原因。
    /// </summary>
    public void SetError(string reason) { HasError = true; ErrorReason = reason; }

    /// <summary>
    /// 是否命中了该模型内已存在的 Key（preview 阶段查库比对）
    /// </summary>
    public bool IsConflict { get; set; }
}

/// <summary>
/// 导入预览结果（preview 接口返回，供前端展示与确认）
/// </summary>
public class VariableImportPreviewDto
{
    public int ModelId { get; set; }

    /// <summary>
    /// 文件解析出的总行数（含错误行）
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// 可导入的有效行数（无错误、非冲突）
    /// </summary>
    public int ValidRows { get; set; }

    /// <summary>
    /// 解析失败的行数
    /// </summary>
    public int ErrorRows { get; set; }

    /// <summary>
    /// 命中了模型内既有 Key 的行数
    /// </summary>
    public int ConflictRows { get; set; }

    public List<VariableImportRow> Rows { get; set; } = new();
}

/// <summary>
/// 导入结果（确认导入后返回，含各项计数与失败明细）
/// </summary>
public class VariableImportResultDto
{
    public int Inserted { get; set; }

    /// <summary>
    /// 覆盖更新的行数
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// 被跳过的行数（含冲突跳过与错误行）
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// 失败行数
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// 失败行明细（含原因）
    /// </summary>
    public List<VariableImportRow> FailedRows { get; set; } = new();
}