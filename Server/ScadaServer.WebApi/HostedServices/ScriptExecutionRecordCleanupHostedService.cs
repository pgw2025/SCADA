using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 脚本执行记录自动清理托管服务。
    /// <para>
    /// 每天凌晨 3:00 执行一次，按系统配置的 RetentionPeriodDays（与报警记录/系统日志清理一致，默认 90 天）
    /// 分批删除超过保留期的脚本执行记录，避免 ScriptExecutionRecords 表（无外键、数据量大）无限增长。
    /// 采用批删除 + 批间延迟，避免单条大事务长锁影响运行时写入。
    /// </para>
    /// <para>实现：每小时轮询一次，仅在「本地时间 3 点且当天尚未执行」时触发清理。</para>
    /// </summary>
    public class ScriptExecutionRecordCleanupHostedService : BackgroundService
    {
        private const int BatchSize = 2000;
        private const int BatchDelayMs = 200;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScriptExecutionRecordCleanupHostedService> _logger;
        private readonly DatabaseInitializationStatus _dbReady;

        private DateTime? _lastCleanupDate;

        public ScriptExecutionRecordCleanupHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<ScriptExecutionRecordCleanupHostedService> logger,
            DatabaseInitializationStatus dbReady)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dbReady = dbReady;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                var dbResult = await _dbReady.WaitAsync(stoppingToken);
                if (!dbResult.Succeeded)
                {
                    _logger.LogWarning("数据库初始化未完成，脚本执行记录清理服务退出。");
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
                    if (now.Hour == 3 && _lastCleanupDate != now.Date)
                    {
                        _lastCleanupDate = now.Date;
                        try
                        {
                            await CleanupAsync(now, stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "脚本执行记录自动清理失败。");
                        }
                    }

                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出
            }
        }

        private async Task CleanupAsync(DateTime now, CancellationToken token)
        {
            var retentionDays = await GetRetentionDaysAsync(token);
            var cutoff = now.AddDays(-retentionDays);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

            var total = 0;
            while (!token.IsCancellationRequested)
            {
                var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM `ScriptExecutionRecords` WHERE `StartedAt` < {cutoff} ORDER BY `Id` LIMIT {BatchSize}",
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
                _logger.LogInformation("脚本执行记录清理：清理 {Count} 条（保留 {Days} 天）。", total, retentionDays);
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