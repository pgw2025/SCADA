using ScadaServer.Domain.Entities;

namespace ScadaServer.Application.DTOs;

/// <summary>
/// ModelVariable（变量模板实体）→ <see cref="ModelVariableDto"/> 的单一映射来源，
/// 供数据模型服务与模型变量服务共用，避免字段漂移（如漏映射 StoreIntervalMs / Scale / DeadBand）。
/// </summary>
public static class ModelVariableMapper
{
    public static ModelVariableDto ToDto(ModelVariable v) => new()
    {
        Id = v.Id,
        ModelId = v.ModelId,
        Key = v.Key,
        Name = v.Name,
        Type = v.Type,
        DataType = v.DataType,
        Unit = v.Unit,
        Min = v.Min,
        Max = v.Max,
        Description = v.Description,
        IsStored = v.IsStored,
        StoreMode = v.StoreMode,
        StoreIntervalMs = v.StoreIntervalMs,
        UpdateMode = v.UpdateMode,
        ScaleExpression = v.ScaleExpression,
        DeadBand = v.DeadBand,
        IsReadOnly = v.IsReadOnly,
        ExtensionData = v.ExtensionData
    };
}