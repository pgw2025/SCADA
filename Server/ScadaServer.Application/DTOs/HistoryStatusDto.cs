namespace ScadaServer.Application.DTOs;

/// <summary>
/// 历史库当前生效状态（GET /api/scada/history/status 响应）。
/// <para>用于前端展示「当前生效后端」，消除“以为在用 Influx 实际在用 MySQL”的歧义（阶段1 P1-1）。</para>
/// </summary>
public class HistoryStatusDto
{
    /// <summary>当前生效后端：InfluxDB 或 MySQL。</summary>
    public string Backend { get; set; } = "MySQL";

    /// <summary>InfluxDB 是否已配置生效。</summary>
    public bool InfluxConfigured { get; set; }

    /// <summary>生效的 InfluxDB 配置概要（不含敏感字段）。未配置时为 null。</summary>
    public HistoryInfluxInfoDto? Influx { get; set; }

    /// <summary>历史写入路径运行期统计（阶段2）。</summary>
    public HistoryWritePathDto WritePath { get; set; } = new();
}

/// <summary>
/// 历史写入路径运行期统计。
/// </summary>
public class HistoryWritePathDto
{
    /// <summary>当前队列剩余容量（待写条目数）</summary>
    public int QueueDepth { get; set; }

    /// <summary>累计入队采样点数</summary>
    public long EnqueuedTotal { get; set; }

    /// <summary>队列满丢弃采样点数</summary>
    public long DroppedQueueFull { get; set; }

    /// <summary>双后端重试穷尽且补偿溢出丢弃的采样点数</summary>
    public long DroppedAllBackendFailed { get; set; }

    /// <summary>NaN/Infinity 采样点分流计数（改道 MySQL，非丢弃）</summary>
    public long InvalidValuePoints { get; set; }

    /// <summary>补偿队列当前深度（条）</summary>
    public int RetryBufferDepth { get; set; }

    /// <summary>最近一次落库时间（UTC）</summary>
    public DateTime? LastFlushAt { get; set; }

    /// <summary>最近一次成功落库时间（UTC）</summary>
    public DateTime? LastWriteSucceededAt { get; set; }
}

/// <summary>
/// 生效 InfluxDB 配置概要（脱敏）。仅回显 Name/Host/Port/Bucket/Org/LastStatus，不含 Token/Password。
/// </summary>
public class HistoryInfluxInfoDto
{
    /// <summary>配置名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>主机地址</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>端口号</summary>
    public int Port { get; set; }

    /// <summary>Bucket（或 DatabaseName 兜底）</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>组织</summary>
    public string? Org { get; set; }

    /// <summary>最近一次连接状态</summary>
    public string? LastStatus { get; set; }
}
