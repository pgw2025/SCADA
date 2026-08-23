using Microsoft.Extensions.Hosting;
using ScadaServer.Application.DTOs;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScadaServer.Infrastructure.Services;

/// <summary>
/// 系统监控服务，后台运行收集系统性能指标
/// </summary>
public class SystemMonitorService : BackgroundService
{
    /// <summary>
    /// 总轮询包数（静态计数器）
    /// </summary>
    private static long _totalPollPackets = 0;

    /// <summary>
    /// 获取总轮询包数
    /// </summary>
    public static long TotalPollPackets => _totalPollPackets;

    /// <summary>
    /// 当前系统状态
    /// </summary>
    public SystemStatusDto CurrentStatus { get; private set; } = new();

    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _ramCounter;
    private readonly PerformanceCounter? _diskTimeCounter;

    private readonly List<PerformanceCounter> _netInCounters = new();
    private readonly List<PerformanceCounter> _netOutCounters = new();

    /// <summary>
    /// 增加轮询包计数
    /// </summary>
    public static void IncrementPollPackets()
    {
        Interlocked.Increment(ref _totalPollPackets);
    }

    /// <summary>
    /// 初始化系统监控服务
    /// </summary>
    public SystemMonitorService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                _diskTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");

                var category = new PerformanceCounterCategory("Network Interface");
                foreach (var instance in category.GetInstanceNames())
                {
                    // 过滤掉虚拟或不稳定的接口
                    if (instance.Contains("Teredo") || instance.Contains("Loopback") || instance.Contains("Pseudo-Interface"))
                        continue;

                    try
                    {
                        var inCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance);
                        var outCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance);

                        // 初次调用以初始化
                        inCounter.NextValue();
                        outCounter.NextValue();

                        _netInCounters.Add(inCounter);
                        _netOutCounters.Add(outCounter);
                    }
                    catch
                    {
                        // 忽略无法初始化的接口
                    }
                }
            }
            catch (Exception)
            {
                // In case of insufficient permissions or system limitations
            }
        }
    }

    /// <summary>
    /// 安全求和性能计数器
    /// </summary>
    private double SafeSumCounters(IEnumerable<PerformanceCounter> counters)
    {
        double sum = 0;
        foreach (var counter in counters)
        {
            try
            {
                sum += counter.NextValue();
            }
            catch
            {
                // 忽略不存在或失效的计数器
            }
        }
        return sum;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var process = Process.GetCurrentProcess();

        while (!stoppingToken.IsCancellationRequested)
        {
            var uptime = DateTime.Now - process.StartTime;

            double cpu = 0.0;
            double memPercent = 0.0;
            double diskTime = 0.0;
            double netIn = 0.0;
            double netOut = 0.0;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    cpu = _cpuCounter?.NextValue() ?? 0.0;
                }
                catch { }

                var memInfo = GC.GetGCMemoryInfo();
                long totalBytes = memInfo.TotalAvailableMemoryBytes;
                float availableMb = _ramCounter?.NextValue() ?? 0.0f;
                double totalMb = totalBytes / (1024.0 * 1024.0);

                if (totalMb > 0)
                {
                    memPercent = ((totalMb - availableMb) / totalMb) * 100;
                }

                try
                {
                    diskTime = _diskTimeCounter?.NextValue() ?? 0.0;
                }
                catch { }

                netIn = SafeSumCounters(_netInCounters) / 1024.0; // KB/s
                netOut = SafeSumCounters(_netOutCounters) / 1024.0; // KB/s
            }

            CurrentStatus = new SystemStatusDto
            {
                CpuUsage = Math.Round(cpu, 2),
                MemUsage = Math.Round(memPercent, 2),
                DiskLoadPercentage = Math.Round(diskTime, 2),
                NetworkIn = Math.Round(netIn, 2),
                NetworkOut = Math.Round(netOut, 2),
                UptimeDays = uptime.Days,
                UptimeHours = uptime.Hours,
                UptimeMins = uptime.Minutes,
                PollFreq = 2000,
                TotalPollPackets = TotalPollPackets,
                Disks = GetDiskMetrics()
            };

            await Task.Delay(2000, stoppingToken);
        }
    }

    /// <summary>
    /// 获取磁盘指标
    /// </summary>
    private List<DiskInfoDto> GetDiskMetrics()
    {
        var diskList = new List<DiskInfoDto>();
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady);

        foreach (var drive in drives)
        {
            long totalSize = drive.TotalSize;
            long freeSpace = drive.AvailableFreeSpace;
            long usedSize = totalSize - freeSpace;

            diskList.Add(new DiskInfoDto
            {
                Name = drive.Name,
                Label = drive.VolumeLabel,
                TotalSizeGb = totalSize / 1024 / 1024 / 1024,
                UsedSizeGb = usedSize / 1024 / 1024 / 1024,
                UsagePercentage = Math.Round((double)usedSize / totalSize * 100, 2)
            });
        }
        return diskList;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
        _diskTimeCounter?.Dispose();
        foreach (var c in _netInCounters) c.Dispose();
        foreach (var c in _netOutCounters) c.Dispose();
        base.Dispose();
    }
}
