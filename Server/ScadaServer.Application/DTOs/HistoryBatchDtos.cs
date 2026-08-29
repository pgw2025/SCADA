namespace ScadaServer.Application.DTOs;

/// <summary>
/// 批量历史查询：单个待查变量（DeviceKey + VariableKey 唯一标识，避免跨设备同名变量混入）。
/// </summary>
public class HistoryBatchVariableDto
{
    /// <summary>设备标识（区分不同设备的同名变量）</summary>
    public string DeviceKey { get; set; } = string.Empty;

    /// <summary>变量业务键</summary>
    public string VariableKey { get; set; } = string.Empty;
}

/// <summary>
/// 批量历史查询请求。
/// </summary>
public class HistoryBatchRequestDto
{
    /// <summary>待查变量列表（上限 8）</summary>
    public List<HistoryBatchVariableDto> Variables { get; set; } = new();

    /// <summary>每变量返回条数上限（1~10000，默认 1000）</summary>
    public int Limit { get; set; } = 1000;

    /// <summary>起始时间（UTC，可选）</summary>
    public DateTime? Start { get; set; }

    /// <summary>结束时间（UTC，可选）</summary>
    public DateTime? End { get; set; }

    /// <summary>聚合窗口（毫秒，可选）。>0 时按窗口聚合降采样。</summary>
    public long? AggregateWindowMs { get; set; }

    /// <summary>聚合函数（mean/max/min/first/last，默认 mean）</summary>
    public string AggregateFn { get; set; } = "mean";
}

/// <summary>
/// 批量历史查询结果项：单个变量的完整历史序列。
/// </summary>
public class HistoryBatchItemDto
{
    /// <summary>设备标识</summary>
    public string DeviceKey { get; set; } = string.Empty;

    /// <summary>变量业务键</summary>
    public string VariableKey { get; set; } = string.Empty;

    /// <summary>变量名称</summary>
    public string VariableName { get; set; } = string.Empty;

    /// <summary>历史序列（按时间升序）</summary>
    public List<HistoryRecordDto> Records { get; set; } = new();
}

/// <summary>
/// 批量历史查询响应。
/// </summary>
public class HistoryBatchResponseDto
{
    /// <summary>各变量的历史序列</summary>
    public List<HistoryBatchItemDto> Items { get; set; } = new();
}
