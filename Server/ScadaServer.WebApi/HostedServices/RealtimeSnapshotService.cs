using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 实时快照服务（单例 + IHostedService，MySQL 实时库）。
    /// <para>
    /// 采集循环通过 <see cref="IRealtimeSnapshotService.Update"/> 非阻塞更新内存快照
    /// （每设备每变量最新一行），本服务周期性（默认 1s）将全部快照批量 Upsert 到
    /// VariableRealtime 表。Upsert 采用「批量查已存在键 → 新增 + 更新」两步，避免
    /// 每变量独立查询；写入失败仅记日志，下轮继续，不阻塞采集。
    /// </para>
    /// </summary>
    public class RealtimeSnapshotService : IRealtimeSnapshotService, IHostedService
    {
        private const int FlushIntervalMs = 1000;

        private readonly ConcurrentDictionary<string, VariableRealtime> _snapshots = new();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RealtimeSnapshotService> _logger;
        private readonly DatabaseInitializationStatus _dbReady;
        private readonly CancellationTokenSource _cts = new();

        public RealtimeSnapshotService(
            IServiceScopeFactory scopeFactory,
            ILogger<RealtimeSnapshotService> logger,
            DatabaseInitializationStatus dbReady)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dbReady = dbReady;
        }

        /// <inheritdoc/>
        public void Update(
            int deviceId,
            string deviceKey,
            string variableKey,
            string variableName,
            double value,
            string? rawValue,
            string? quality,
            DateTime timestamp)
        {
            var key = BuildKey(deviceId, variableKey);
            var snapshot = new VariableRealtime
            {
                DeviceId = deviceId,
                DeviceKey = deviceKey,
                VariableKey = variableKey,
                VariableName = variableName,
                Value = value,
                RawValue = rawValue,
                Quality = quality,
                Timestamp = timestamp
            };

            // 值更新以最新采集为准，直接覆盖（同一 key 由单设备单变量产生，无并发写竞态需求）。
            _snapshots[key] = snapshot;
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(() => ProcessAsync(_cts.Token));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();
            return Task.CompletedTask;
        }

        private async Task ProcessAsync(CancellationToken token)
        {
            try
            {
                var dbResult = await _dbReady.WaitAsync(token);
                if (!dbResult.Succeeded)
                {
                    _logger.LogWarning("数据库初始化未完成，实时快照服务退出（本次不写入）。");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(FlushIntervalMs, token);
                    await FlushAsync(token);
                }
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出路径
            }

            // 停止前最后落一次快照，避免丢失最近数据
            try
            {
                await FlushAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "实时快照服务停止时刷新剩余数据失败。");
            }
        }

        private async Task FlushAsync(CancellationToken token)
        {
            if (_snapshots.IsEmpty)
            {
                return;
            }

            // 取出当前全部快照（最新的覆盖结果），清空待写集合。
            var toWrite = new List<VariableRealtime>(_snapshots.Count);
            foreach (var pair in _snapshots)
            {
                toWrite.Add(pair.Value);
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

                // 批量取已存在键（复合主键），一次性区分新增与更新。
                var deviceIds = toWrite.Select(s => s.DeviceId).Distinct().ToList();
                var existingKeys = new HashSet<(int DeviceId, string VariableKey)>(
                    await db.VariableRealtimes
                        .Where(r => deviceIds.Contains(r.DeviceId))
                        .Select(r => new { r.DeviceId, r.VariableKey })
                        .ToListAsync(token)
                        .ContinueWith(t => t.Result.Select(r => (r.DeviceId, r.VariableKey)), token));

                var inserts = new List<VariableRealtime>();
                var updates = new List<VariableRealtime>();
                foreach (var snapshot in toWrite)
                {
                    if (existingKeys.Contains((snapshot.DeviceId, snapshot.VariableKey)))
                    {
                        updates.Add(snapshot);
                    }
                    else
                    {
                        inserts.Add(snapshot);
                    }
                }

                if (updates.Count > 0)
                {
                    foreach (var snapshot in updates)
                    {
                        var entity = await db.VariableRealtimes.FindAsync(
                            new object[] { snapshot.DeviceId, snapshot.VariableKey }, token);
                        if (entity == null)
                        {
                            inserts.Add(snapshot);
                            continue;
                        }

                        entity.DeviceKey = snapshot.DeviceKey;
                        entity.VariableName = snapshot.VariableName;
                        entity.Value = snapshot.Value;
                        entity.RawValue = snapshot.RawValue;
                        entity.Quality = snapshot.Quality;
                        entity.Timestamp = snapshot.Timestamp;
                    }
                }

                if (inserts.Count > 0)
                {
                    db.VariableRealtimes.AddRange(inserts);
                }

                if (updates.Count > 0 || inserts.Count > 0)
                {
                    await db.SaveChangesAsync(token);
                    _logger.LogDebug("已刷新实时快照 {Total} 行（新增 {Inserts} / 更新 {Updates}）。",
                        toWrite.Count, inserts.Count, updates.Count);
                }
            }
            catch (Exception ex)
            {
                // 写入失败不重试，避免阻塞；下轮 Flush 会重写全部快照。
                _logger.LogWarning(ex, "实时快照批量写入失败（{Count} 行，下轮重试）。", toWrite.Count);
            }
        }

        private static string BuildKey(int deviceId, string variableKey) => $"{deviceId}:{variableKey}";
    }
}
