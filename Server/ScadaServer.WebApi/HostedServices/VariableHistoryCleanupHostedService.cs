using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.Options;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// MySQL 历史数据自动清理托管服务。
    /// <para>
    /// 每天凌晨 3:30 执行一次，按 <c>History:MySqlRetentionDays</c>（默认 0 = 不启用）删除
    /// <c>VariableHistories</c> 表中超过保留期的旧数据。分批删除（每批 LIMIT 2000，批间短暂延迟），
    /// 避免单条大事务长锁表。
    /// </para>
    /// <para>
    /// 实现：每小时轮询一次，仅在「本地时间 3:30 且当天尚未执行」时触发清理。
    /// </para>
    /// </summary>
    public class VariableHistoryCleanupHostedService : BackgroundService
    {
        private const int BatchSize = 2000;
        private const int BatchDelayMs = 200;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VariableHistoryCleanupHostedService> _logger;
        private readonly HistoryRetentionOptions _options;
        private readonly DatabaseInitializationStatus _dbReady;

        private DateTime? _lastCleanupDate;

        public VariableHistoryCleanupHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<VariableHistoryCleanupHostedService> logger,
            IOptions<HistoryRetentionOptions> options,
            DatabaseInitializationStatus dbReady)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
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
                    _logger.LogWarning("数据库初始化未完成，MySQL 历史数据清理服务退出。");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("MySQL 历史数据清理服务在等待数据库初始化时被取消，退出。");
                return;
            }

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var now = DateTime.Now;
                    // 每天 3:30 执行一次（本地时间，与系统日志清理 3:00 错峰）
                    if (now.Hour == 3 && now.Minute >= 30 && _lastCleanupDate != now.Date)
                    {
                        _lastCleanupDate = now.Date;
                        try
                        {
                            await CleanupAsync(stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogTrace("MySQL 历史数据清理在应用关闭期间被取消，向上传播。");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "MySQL 历史数据自动清理失败。");
                        }
                    }

                    // 每小时检查一次
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出
                _logger.LogDebug("MySQL 历史数据清理服务后台循环收到取消信号（应用关闭），正常退出。");
            }
        }

        private async Task CleanupAsync(CancellationToken token)
        {
            var days = _options.MySqlRetentionDays;
            if (days <= 0)
            {
                return; // 默认关闭，升级无副作用
            }

            // cutoff 用 UTC 计算，与 VariableHistories.Timestamp（采集时间 UTC）基准一致。
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var total = 0;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

            // 分批删除：MySQL DELETE ... ORDER BY Id LIMIT n，避免一次性大事务。
            while (true)
            {
                var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM `VariableHistory` WHERE `Timestamp` < {cutoff} ORDER BY `Id` LIMIT {BatchSize}",
                    token);
                if (deleted <= 0)
                {
                    break;
                }
                total += deleted;
                if (deleted < BatchSize)
                {
                    break;
                }
                await Task.Delay(BatchDelayMs, token);
            }

            if (total > 0)
            {
                _logger.LogInformation(
                    "MySQL 历史数据清理：清理 {Count} 条（保留 {Days} 天）。",
                    total,
                    days);
            }
        }
    }
}
