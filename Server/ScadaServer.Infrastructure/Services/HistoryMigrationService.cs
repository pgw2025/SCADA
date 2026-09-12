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
    /// <para>
    /// 阶段4：迁移后台化（HTTP 立即返回）+ 断点续传（<c>DatabaseConfig.MigrateLastId</c> 持久化断点）
    /// + 进度/取消 API。迁移状态持久化到生效历史库配置行上。
    /// </para>
    /// </summary>
    public class HistoryMigrationService : IHistoryMigrationService
    {
        private const int ReadBatchSize = 2000;   // 每批从 MySQL 拉取行数
        private const int WriteChunkSize = 500;   // 每片写入 InfluxDB 的点数（避免单请求体过大）
        private const int WriteRetry = 3;         // Influx 单片写入失败重试次数
        private const int WriteRetryDelayMs = 2000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IInfluxStore _influxStore;
        private readonly ILogger<HistoryMigrationService> _logger;

        private readonly SemaphoreSlim _lock = new(1, 1);

        // ---- 后台任务状态（仅由持有 _lock 的线程访问；读取用 IsRunning()）----
        private bool _isRunning;
        private CancellationTokenSource? _cts;
        private MigrationProgress? _progress;

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
            // 已有任务在跑则直接返回当前状态。
            if (!await _lock.WaitAsync(0))
            {
                return BuildStatusResult("已有历史数据迁移任务正在执行中。");
            }

            try
            {
                // 解析生效配置并校验
                using var scope = _scopeFactory.CreateScope();
                var dbConfigRepo = scope.ServiceProvider.GetRequiredService<IDatabaseConfigRepository>();
                var influxConfig = await ResolveActiveInfluxConfigAsync(dbConfigRepo);
                if (influxConfig == null)
                {
                    return new HistoryMigrationResult
                    {
                        IsRunning = false,
                        Status = "NeverStarted",
                        Message = "未找到生效的 InfluxDB 历史库配置（Type=Historical 且 BackendType=InfluxDB 且 IsActive=true）。"
                    };
                }

                // 迁移前将 InfluxStore 重建到生效配置
                _influxStore.Rebuild(influxConfig);
                if (!_influxStore.IsConfigured)
                {
                    return new HistoryMigrationResult
                    {
                        IsRunning = false,
                        Status = "NeverStarted",
                        Message = "InfluxDB 客户端初始化失败，请检查生效的历史库配置。"
                    };
                }

                // 启动后台迁移任务（锁已持有，_isRunning 置位后立即返回）
                _cts = new CancellationTokenSource();
                _progress = new MigrationProgress
                {
                    StartedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _isRunning = true;

                // 标记状态 Running（落库）
                influxConfig.MigrateStatus = "Running";
                await dbConfigRepo.UpdateAsync(influxConfig);

                // 后台执行（不 await，立即返回启动结果）
                _ = ExecuteMigrationAsync(influxConfig.Id, _cts.Token);

                return new HistoryMigrationResult
                {
                    IsRunning = true,
                    Status = "Running",
                    StartedAt = _progress.StartedAt,
                    Message = "历史数据迁移已启动。"
                };
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<HistoryMigrationResult> GetStatusAsync()
        {
            // 内存态优先（运行中直接返回内存进度）
            var progress = _progress;
            if (_isRunning && progress != null)
            {
                return BuildStatusResult(string.Empty);
            }

            // 无运行任务：读持久化断点状态
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDatabaseConfigRepository>();
            var config = await ResolveActiveInfluxConfigAsync(repo);
            if (config == null)
            {
                return new HistoryMigrationResult
                {
                    IsRunning = false,
                    Status = "NeverStarted",
                    Message = "未配置生效的 InfluxDB 历史库。"
                };
            }

            return new HistoryMigrationResult
            {
                IsRunning = false,
                Status = config.MigrateStatus ?? "NeverStarted",
                LastId = config.MigrateLastId ?? 0,
                UpdatedAt = config.MigrateUpdatedAt,
                Message = config.MigrateStatus switch
                {
                    "Completed" => $"历史数据迁移已完成（最后 Id={config.MigrateLastId}）。",
                    "Interrupted" => $"历史数据迁移曾中断于 Id={config.MigrateLastId}，可再次触发续传。",
                    _ => "尚未开始历史数据迁移。"
                }
            };
        }

        /// <inheritdoc/>
        public async Task<HistoryMigrationResult> CancelAsync()
        {
            var cts = _cts;
            if (!_isRunning || cts == null)
            {
                return await GetStatusAsync();
            }

            cts.Cancel();
            return new HistoryMigrationResult
            {
                IsRunning = true,
                Status = "Running",
                Message = "已请求取消，将在下一片边界生效。"
            };
        }

        /// <summary>组装运行中的状态结果（含内存进度 + 速率/ETA 估算）。</summary>
        private HistoryMigrationResult BuildStatusResult(string message)
        {
            var progress = _progress;
            if (progress == null)
            {
                return new HistoryMigrationResult { IsRunning = false, Status = "NeverStarted", Message = message };
            }

            double speed = 0;
            long eta = 0;
            var elapsed = (DateTime.UtcNow - progress.StartedAt).TotalSeconds;
            if (elapsed > 1)
            {
                speed = progress.Migrated / elapsed;
            }
            if (speed > 0 && progress.Total > progress.Migrated)
            {
                eta = (long)((progress.Total - progress.Migrated) / speed);
            }

            return new HistoryMigrationResult
            {
                IsRunning = _isRunning,
                Status = _isRunning ? "Running" : "Interrupted",
                Total = progress.Total,
                Migrated = progress.Migrated,
                LastId = progress.LastId,
                StartedAt = progress.StartedAt,
                UpdatedAt = progress.UpdatedAt,
                CurrentSpeedPerSec = speed,
                EtaSeconds = eta,
                Message = message
            };
        }

        /// <summary>后台迁移执行主体（从断点续传；片边界响应取消；写失败重试后标记 Interrupted）。</summary>
        private async Task ExecuteMigrationAsync(int configId, CancellationToken token)
        {
            var progress = _progress!;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var historyRepo = scope.ServiceProvider.GetRequiredService<IVariableHistoryRepository>();
                var dbConfigRepo = scope.ServiceProvider.GetRequiredService<IDatabaseConfigRepository>();

                progress.Total = await historyRepo.CountAsync();

                // 断点续传：从上次持久化的 MigrateLastId 之后继续。
                var lastId = await LoadMigrateLastIdAsync(dbConfigRepo, configId);
                progress.LastId = lastId;
                long migrated = progress.Migrated;

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    var batch = await historyRepo.GetBatchAfterIdAsync(lastId, ReadBatchSize);
                    if (batch == null || batch.Count == 0)
                    {
                        break;
                    }

                    // 大页拆小片写入 InfluxDB
                    for (var i = 0; i < batch.Count; i += WriteChunkSize)
                    {
                        token.ThrowIfCancellationRequested();

                        var chunk = batch.GetRange(i, Math.Min(WriteChunkSize, batch.Count - i));
                        var ok = await WriteChunkWithRetryAsync(chunk, token);
                        if (!ok)
                        {
                            await MarkInterruptedAsync(dbConfigRepo, configId, lastId);
                            progress.Status = "Interrupted";
                            _logger.LogWarning("迁移中断于 Id={LastId}：InfluxDB 写入失败（本片 {Count} 条未成功）。", lastId, chunk.Count);
                            return;
                        }
                        migrated += chunk.Count;
                    }

                    lastId = batch[batch.Count - 1].Id;
                    progress.LastId = lastId;
                    progress.Migrated = migrated;
                    progress.UpdatedAt = DateTime.UtcNow;

                    // 每批持久化断点
                    await PersistProgressAsync(dbConfigRepo, configId, lastId, "Running");

                    if (migrated % (ReadBatchSize * 10) == 0)
                    {
                        _logger.LogInformation("历史迁移进度：已迁移 {Migrated}/{Total} 条。", migrated, progress.Total);
                    }
                }

                // 全部完成
                await PersistProgressAsync(dbConfigRepo, configId, lastId, "Completed");
                progress.Status = "Completed";
                progress.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("历史数据迁移完成：共 {Total} 条，成功写入 InfluxDB {Migrated} 条。", progress.Total, migrated);
            }
            catch (OperationCanceledException)
            {
                // 取消：标记 Interrupted，保留断点
                using var scope = _scopeFactory.CreateScope();
                var dbConfigRepo = scope.ServiceProvider.GetRequiredService<IDatabaseConfigRepository>();
                await MarkInterruptedAsync(dbConfigRepo, configId, progress.LastId);
                progress.Status = "Interrupted";
                _logger.LogInformation("历史迁移已取消（断点 Id={LastId}）。", progress.LastId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "历史数据迁移任务执行失败。");
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbConfigRepo = scope.ServiceProvider.GetRequiredService<IDatabaseConfigRepository>();
                    await MarkInterruptedAsync(dbConfigRepo, configId, progress.LastId);
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "迁移失败后标记 Interrupted 状态失败。");
                }
                progress.Status = "Interrupted";
            }
            finally
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>Influx 单片写入（有限重试），返回是否成功。</summary>
        private async Task<bool> WriteChunkWithRetryAsync(List<VariableHistory> chunk, CancellationToken token)
        {
            for (var attempt = 1; attempt <= WriteRetry; attempt++)
            {
                var ok = await _influxStore.WriteAsync(chunk);
                if (ok)
                {
                    return true;
                }
                if (attempt < WriteRetry)
                {
                    try
                    {
                        await Task.Delay(WriteRetryDelayMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }
            }
            return false;
        }

        /// <summary>读取持久化断点（MigrateLastId）。</summary>
        private static async Task<long> LoadMigrateLastIdAsync(IDatabaseConfigRepository repo, int configId)
        {
            var config = await repo.GetByIdAsync(configId);
            return config?.MigrateLastId ?? 0;
        }

        /// <summary>持久化迁移进度（断点 + 状态 + 更新时间）。</summary>
        private static async Task PersistProgressAsync(
            IDatabaseConfigRepository repo, int configId, long lastId, string status)
        {
            var config = await repo.GetByIdAsync(configId);
            if (config == null) return;
            config.MigrateLastId = lastId;
            config.MigrateStatus = status;
            config.MigrateUpdatedAt = DateTime.UtcNow;
            await repo.UpdateAsync(config);
        }

        /// <summary>标记中断状态（保留断点）。</summary>
        private static async Task MarkInterruptedAsync(IDatabaseConfigRepository repo, int configId, long lastId)
        {
            var config = await repo.GetByIdAsync(configId);
            if (config == null) return;
            config.MigrateLastId = lastId;
            config.MigrateStatus = "Interrupted";
            config.MigrateUpdatedAt = DateTime.UtcNow;
            await repo.UpdateAsync(config);
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

        /// <summary>迁移进度内存快照。</summary>
        private sealed class MigrationProgress
        {
            public long Total { get; set; }
            public long Migrated { get; set; }
            public long LastId { get; set; }
            public string Status { get; set; } = "Running";
            public DateTime StartedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }
    }
}
