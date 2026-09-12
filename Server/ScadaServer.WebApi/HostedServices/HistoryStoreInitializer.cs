using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 历史库客户端启动初始化服务（单例 + IHostedService）。
    /// <para>
    /// 服务启动时从 <c>DatabaseConfigs</c> 表加载生效的 InfluxDB 历史库配置（Type=Historical 且
    /// BackendType=InfluxDB 且 IsActive=true），并灌入 <see cref="IInfluxStore"/>，使 InfluxDB
    /// 在重启后即刻生效，无需依赖手动触发历史迁移。
    /// </para>
    /// <para>
    /// 初始化失败（数据库未就绪/无生效配置/Rebuild 失败）仅告警并回退 MySQL 模式，不阻塞服务启动。
    /// </para>
    /// </summary>
    public class HistoryStoreInitializer : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IInfluxStore _influxStore;
        private readonly ILogger<HistoryStoreInitializer> _logger;
        private readonly DatabaseInitializationStatus _dbReady;

        public HistoryStoreInitializer(
            IServiceScopeFactory scopeFactory,
            IInfluxStore influxStore,
            ILogger<HistoryStoreInitializer> logger,
            DatabaseInitializationStatus dbReady)
        {
            _scopeFactory = scopeFactory;
            _influxStore = influxStore;
            _logger = logger;
            _dbReady = dbReady;
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            // 后台异步初始化，不阻塞宿主启动。
            _ = InitializeAsync(cancellationToken);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task InitializeAsync(CancellationToken token)
        {
            try
            {
                var dbResult = await _dbReady.WaitAsync(token);
                if (!dbResult.Succeeded)
                {
                    _logger.LogWarning("数据库初始化未完成，历史库客户端启动初始化跳过（历史数据落 MySQL）。");
                    return;
                }

                DatabaseConfig? config;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IDatabaseConfigRepository>();
                    config = await ResolveActiveInfluxConfigAsync(repo);
                }

                if (config == null)
                {
                    _logger.LogInformation("未配置生效的 InfluxDB 历史库，历史数据落 MySQL。");
                    return;
                }

                _influxStore.Rebuild(config);
                if (_influxStore.IsConfigured)
                {
                    _logger.LogInformation(
                        "历史库客户端启动初始化完成：已启用 InfluxDB（Bucket={Bucket}）。",
                        string.IsNullOrWhiteSpace(config.Bucket) ? config.DatabaseName : config.Bucket);
                }
                else
                {
                    _logger.LogWarning(
                        "历史库客户端启动初始化失败（InfluxDB 未生效），历史数据落 MySQL。请检查生效历史库配置。");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("历史库客户端启动初始化被取消。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "历史库客户端启动初始化异常，历史数据落 MySQL。");
            }
        }

        /// <summary>
        /// 解析当前生效的 InfluxDB 历史库配置（同 Type 仅一条 IsActive）。找不到返回 null。
        /// 筛选逻辑与 <see cref="ScadaServer.Infrastructure.Services.HistoryMigrationService"/> 保持一致。
        /// </summary>
        private static async Task<DatabaseConfig?> ResolveActiveInfluxConfigAsync(IDatabaseConfigRepository repo)
        {
            var list = await repo.GetListAsync();
            return list.FirstOrDefault(c =>
                string.Equals(c.Type, "Historical", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.BackendType, "InfluxDB", StringComparison.OrdinalIgnoreCase) &&
                c.IsActive);
        }
    }
}
