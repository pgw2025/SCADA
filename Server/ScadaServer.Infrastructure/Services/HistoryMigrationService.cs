using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Services
{
    /// <summary>
    /// 历史数据迁移服务实现（单例）。
    /// <para>
    /// 通过 <see cref="IServiceScopeFactory"/> 在每次迁移时开启作用域解析 scoped 仓储，
    /// 自身单例持有并发锁，保证全局同时仅一任务在跑。
    /// </para>
    /// </summary>
    public class HistoryMigrationService : IHistoryMigrationService
    {
        private const int ReadBatchSize = 2000;   // 每批从 MySQL 拉取行数
        private const int WriteChunkSize = 500;   // 每片写入 InfluxDB 的点数（避免单请求体过大）

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IInfluxStore _influxStore;
        private readonly ILogger<HistoryMigrationService> _logger;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _isRunning;

        public HistoryMigrationService(
            IServiceScopeFactory scopeFactory,
            IInfluxStore influxStore,
            ILogger<HistoryMigrationService> logger)
        {
            _scopeFactory = scopeFactory;
            _influxStore = influxStore;
            _logger = logger;
        }

        /// <inheritdoc/>
        public bool IsRunning() => _isRunning;

        /// <inheritdoc/>
        public async Task<HistoryMigrationResult> MigrateAsync()
        {
            // 已有任务在跑则直接返回，避免并发重复迁移。
            if (!await _lock.WaitAsync(0))
            {
                return new HistoryMigrationResult { IsRunning = true, Message = "已有历史数据迁移任务正在执行中。" };
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var historyRepo = scope.ServiceProvider.GetRequiredService<IVariableHistoryRepository>();
                var dbConfigRepo = scope.ServiceProvider.GetRequiredService<IDatabaseConfigRepository>();

                // 解析当前生效的 InfluxDB 历史库配置
                var influxConfig = await ResolveActiveInfluxConfigAsync(dbConfigRepo);
                var result = new HistoryMigrationResult { IsRunning = true };

                if (influxConfig == null)
                {
                    result.Message = "未找到生效的 InfluxDB 历史库配置（Type=Historical 且 BackendType=InfluxDB 且 IsActive=true）。";
                    result.IsRunning = false;
                    return result;
                }

                // 迁移前将 InfluxStore 重建到生效配置（同时修复运行期客户端未被初始化的问题）
                _influxStore.Rebuild(influxConfig);
                if (!_influxStore.IsConfigured)
                {
                    result.Message = "InfluxDB 客户端初始化失败，请检查生效的历史库配置。";
                    result.IsRunning = false;
                    return result;
                }

                // 先取总数用于结果展示
                result.Total = await historyRepo.CountAsync();

                _isRunning = true;
                long migrated = 0;
                long lastId = 0;
                try
                {
                    while (true)
                    {
                        var batch = await historyRepo.GetBatchAfterIdAsync(lastId, ReadBatchSize);
                        if (batch == null || batch.Count == 0)
                        {
                            break;
                        }

                        // 大页拆小片写入 InfluxDB
                        for (var i = 0; i < batch.Count; i += WriteChunkSize)
                        {
                            var chunk = batch.GetRange(i, Math.Min(WriteChunkSize, batch.Count - i));
                            var ok = await _influxStore.WriteAsync(chunk);
                            if (!ok)
                            {
                                var msg = $"迁移中断于 Id={lastId}：InfluxDB 写入失败（本片 {chunk.Count} 条未成功）。";
                                _logger.LogWarning(msg);
                                result.Message = msg;
                                result.Migrated = migrated;
                                return result;
                            }
                            migrated += chunk.Count;
                        }

                        lastId = batch[batch.Count - 1].Id;
                        if (migrated % (ReadBatchSize * 10) == 0)
                        {
                            _logger.LogInformation("历史迁移进度：已迁移 {Migrated}/{Total} 条。", migrated, result.Total);
                        }
                    }

                    result.Migrated = migrated;
                    result.Message = $"历史数据迁移完成：共 {result.Total} 条，成功写入 InfluxDB {migrated} 条。";
                    return result;
                }
                finally
                {
                    _isRunning = false;
                    result.IsRunning = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "历史数据迁移任务执行失败。");
                return new HistoryMigrationResult
                {
                    IsRunning = false,
                    Message = $"历史数据迁移失败：{ex.Message}"
                };
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 解析当前生效的 InfluxDB 历史库配置（同 Type 仅一条 IsActive）。找不到返回 null。
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