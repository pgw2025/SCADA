namespace ScadaServer.Application.DTOs;

public class SystemStatusDto
{
    public double CpuUsage { get; set; }
    public double MemUsage { get; set; }
    public double DiskLoadPercentage { get; set; }
    public double NetworkIn { get; set; }
    public double NetworkOut { get; set; }
    public int UptimeDays { get; set; }
    public int UptimeHours { get; set; }
    public int UptimeMins { get; set; }
    public int PollFreq { get; set; }
    public long TotalPollPackets { get; set; }
    public List<DiskInfoDto> Disks { get; set; } = new();
}

public class DiskInfoDto
{
    public string Name { get; set; } = string.Empty; // 如 "C:\"
    public string Label { get; set; } = string.Empty; // 磁盘标签
    public long TotalSizeGb { get; set; }
    public long UsedSizeGb { get; set; }
    public double UsagePercentage { get; set; }
}
