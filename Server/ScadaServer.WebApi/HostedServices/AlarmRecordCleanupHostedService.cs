using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 报警记录自动清理托管服务。
    /// <para>
    /// 每天凌晨 3:00 执行一次，按系统配置的 RetentionPeriodDays（系统配置-数据保留周期）分批删除
    /// 超过保留期的报警记录，避免 AlarmRecords 表无限增长拖慢查询与写库。
    /// 采用批删除 + 批间延迟（DELETE ... ORDER BY Id LIMIT n），避免单条大事务长锁。
    /// 保留期以"当前未确认/未恢复报警优先保留"为原则：清理仅基于触发时间，不额外跳过未恢复记录，
    /// 以免长期未恢复的过期报警无限占用存储（运维可据实际需求调整）。
    /// </para>
    /// <para>
    /// 实现：每小时轮询一次，仅在「本地时间 3 点且当天尚未执行」时触发清理（与系统日志清理一致）。
    /// </para>
    /// </summary>
    public class AlarmRecordCleanupHostedService : BackgroundService
    {
        private const int BatchSize = 2000;
        private const int BatchDelayMs = 200;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AlarmRecordCleanupHostedService> _logger;
        private readonly DatabaseInitializationStatus _dbReady;

        private DateTime? _lastCleanupDate;

        public AlarmRecordCleanupHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<AlarmRecordCleanupHostedService> logger,
            DatabaseInitializationStatus dbReady)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dbReady = dbReady;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 等待数据库就绪后再开始轮询
            try
            {
                var dbResult = await _dbReady.WaitAsync(stoppingToken);
                if (!dbResult.Succeeded)
                {
                    _logger.LogWarning("数据库初始化未完成，报警记录清理服务退出。");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var now = DateTime.Now;
                    // 每天 3 点执行一次（本地时间）
                    if (now.Hour == 3 && _lastCleanupDate != now.Date)
                    {
                        _lastCleanupDate = now.Date;
                        try
                        {
                            await CleanupAsync(stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "报警记录自动清理失败。");
                        }
                    }

                    // 每小时检查一次
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出
            }
        }

        private async Task CleanupAsync(CancellationToken token)
        {
            // 保留期取系统配置 RetentionPeriodDays（未配置时兜底 90 天）
            var retentionDays = await GetRetentionDaysAsync(token);
            // cutoff 用 UTC 计算，与 AlarmRecords.TriggeredAt（UTC 写入）基准一致
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

            var total = 0;
            // 分批删除，避免一次性大事务长锁 AlarmRecords 表。
            while (!token.IsCancellationRequested)
            {
                var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM `AlarmRecords` WHERE `TriggeredAt` < {cutoff} ORDER BY `Id` LIMIT {BatchSize}",
                    token);
                if (deleted <= 0)
                    break;
                total += deleted;
                if (deleted < BatchSize)
                    break;
                await Task.Delay(BatchDelayMs, token);
            }

            if (total > 0)
            {
                _logger.LogInformation("报警记录清理：清理 {Count} 条（保留 {Days} 天）。", total, retentionDays);
            }
        }

        private async Task<int> GetRetentionDaysAsync(CancellationToken token)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                var config = await db.SystemConfigs
                    .AsNoTracking()
                    .OrderBy(c => c.Id)
                    .FirstOrDefaultAsync(token);
                if (config != null && config.RetentionPeriodDays > 0)
                {
                    return config.RetentionPeriodDays;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取系统配置保留期失败，使用默认值 90 天。");
            }
            return 90;
        }
    }
}