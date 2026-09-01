namespace ScadaServer.Application.DTOs;

/// <summary>
/// 系统运行状态 DTO（CPU/内存/磁盘/网络/运行时长及轮询统计）。
/// </summary>
public class SystemStatusDto
{
    /// <summary>CPU 使用率（百分比 0-100）</summary>
    public double CpuUsage { get; set; }

    /// <summary>内存使用率（百分比 0-100）</summary>
    public double MemUsage { get; set; }

    /// <summary>磁盘负载百分比</summary>
    public double DiskLoadPercentage { get; set; }

    /// <summary>网络接收速率（KB/s，Windows 平台采集）</summary>
    public double NetworkIn { get; set; }

    /// <summary>网络发送速率（KB/s，Windows 平台采集）</summary>
    public double NetworkOut { get; set; }

    /// <summary>运行时长-天</summary>
    public int UptimeDays { get; set; }

    /// <summary>运行时长-小时（天之外）</summary>
    public int UptimeHours { get; set; }

    /// <summary>运行时长-分钟（小时之外）</summary>
    public int UptimeMins { get; set; }

    /// <summary>采集/轮询周期（毫秒，前端以 ms 展示）</summary>
    public int PollFreq { get; set; }

    /// <summary>累计轮询包数</summary>
    public long TotalPollPackets { get; set; }

    /// <summary>磁盘详情列表</summary>
    public List<DiskInfoDto> Disks { get; set; } = new();
}

/// <summary>
/// 磁盘详情 DTO（单个磁盘的容量与使用情况）。
/// </summary>
public class DiskInfoDto
{
    /// <summary>盘符/挂载路径，如 "C:\"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>磁盘标签（卷标）</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>总容量（GB）</summary>
    public long TotalSizeGb { get; set; }

    /// <summary>已用容量（GB）</summary>
    public long UsedSizeGb { get; set; }

    /// <summary>使用率（百分比 0-100）</summary>
    public double UsagePercentage { get; set; }
}
