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
    /// 系统日志自动清理托管服务。
    /// <para>
    /// 每天凌晨 3:00 执行一次，按分类使用不同保留期（Runtime/Operation/Security，读 SystemLog:Retention 配置）。
    /// 分批删除（每批 LIMIT 2000，批间短暂延迟），避免单条大事务长锁 SystemLogs 表。
    /// </para>
    /// <para>
    /// 实现：每小时轮询一次，仅在「本地时间 3 点且当天尚未执行」时触发清理；
    /// 清理服务自身记录一条 runtime 日志（类别在黑名单中，不会递归写回）。
    /// </para>
    /// </summary>
    public class SystemLogCleanupHostedService : BackgroundService
    {
        private static readonly string[] Categories = { "Runtime", "Operation", "Security" };
        private const int BatchSize = 2000;
        private const int BatchDelayMs = 200;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SystemLogCleanupHostedService> _logger;
        private readonly SystemLogOptions _options;
        private readonly DatabaseInitializationStatus _dbReady;

        private DateTime? _lastCleanupDate;

        public SystemLogCleanupHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<SystemLogCleanupHostedService> logger,
            IOptions<SystemLogOptions> options,
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
                    _logger.LogWarning("数据库初始化未完成，系统日志清理服务退出。");
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
                            _logger.LogError(ex, "系统日志自动清理失败。");
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
            var retentions = new Dictionary<string, int>
            {
                ["Runtime"] = Math.Max(_options.Retention.Runtime, 1),
                ["Operation"] = Math.Max(_options.Retention.Operation, 1),
                ["Security"] = Math.Max(_options.Retention.Security, 1)
            };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

            foreach (var category in Categories)
            {
                // cutoff 用 UTC 计算，与 SystemLogs.Timestamp（UTC 写入）基准一致
                var cutoff = DateTime.UtcNow.AddDays(-retentions[category]);
                var total = 0;

                // 分批删除：MySQL DELETE ... ORDER BY Id LIMIT n，避免一次性大事务。
                // ExecuteSqlInterpolatedAsync 会把 category/cutoff 参数化，LIMIT 为编译期常量直接内联。
                while (true)
                {
                    var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM `SystemLogs` WHERE `Category` = {category} AND `Timestamp` < {cutoff} ORDER BY `Id` LIMIT {BatchSize}",
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
                    _logger.LogInformation("系统日志清理：{Category} 清理 {Count} 条（保留 {Days} 天）。", category, total, retentions[category]);
                }
            }
        }
    }
}
