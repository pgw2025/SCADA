using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;

/// <summary>
/// 变量运行时状态类
/// 用于存储和管理设备变量在运行时的实时状态信息
/// </summary>
public class VariableRuntime
{
    /// <summary>
    /// 关联的变量模型定义
    /// </summary>
    public ModelVariable Variable { get; init; }

    /// <summary>
    /// 当前值
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 上一个值（用于检测变化）
    /// </summary>
    public object? PreviousValue { get; set; }

    /// <summary>
    /// 最后更新时间戳
    /// </summary>
    public DateTime UpdateTime { get; set; }

    /// <summary>
    /// 变量质量状态（Good/Bad/Uncertain）
    /// </summary>
    public VariableQuality Quality { get; set; }

    /// <summary>
    /// 值是否发生变化
    /// </summary>
    public bool IsChanged { get; set; }
}
