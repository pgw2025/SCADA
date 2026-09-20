using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text;
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

        /// <summary>单条多值 Upsert 的行数上限（每行 8 列，500 行约 4000 参数，避免超长 SQL 与 max_allowed_packet）。</summary>
        private const int UpsertBatchSize = 500;

        private readonly ConcurrentDictionary<string, VariableRealtime> _snapshots = new();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RealtimeSnapshotService> _logger;
        private readonly DatabaseInitializationStatus _dbReady;
        private readonly CancellationTokenSource _cts = new();

        private Task? _processTask;

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
            // ProcessAsync 本身返回热 Task，无需 Task.Run；保存引用供 StopAsync 等待退出。
            _processTask = ProcessAsync(_cts.Token);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();
            if (_processTask is not null)
            {
                try
                {
                    // 等待循环完成停止前最后一次快照落库；超时兜底防止宿主关闭被拖死。
                    await _processTask.WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("实时快照服务停止超时，最近快照可能未落库。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "实时快照服务后台循环退出异常。");
                }
            }
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
            catch (Exception ex)
            {
                // 未预期异常不能让循环静默死亡（fire-and-forget 时代无法察觉），记录后继续走收尾落库。
                _logger.LogError(ex, "实时快照服务后台循环因未预期异常退出。");
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

            // 取出当前全部快照（最新的覆盖结果）。快照仅做内存覆盖，不在此清空；
            // 同一 key 下轮刷新会再次覆盖，配合 upsert 幂等语义无需清空。
            var toWrite = new List<VariableRealtime>(_snapshots.Count);
            foreach (var pair in _snapshots)
            {
                toWrite.Add(pair.Value);
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

                // 直接用底层 ADO.NET 连接执行参数化多值 upsert，依赖 (DeviceId, VariableKey)
                // 复合主键判定新增/更新，避免逐条 FindAsync 造成的 N+1 查询。
                var conn = db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync(token);
                }

                var written = 0;
                for (var i = 0; i < toWrite.Count; i += UpsertBatchSize)
                {
                    var batch = toWrite.GetRange(i, Math.Min(UpsertBatchSize, toWrite.Count - i));
                    written += await UpsertBatchAsync(conn, batch, token);
                }

                _logger.LogDebug("已刷新实时快照 {Total} 行。", written);
            }
            catch (Exception ex)
            {
                // 写入失败不重试，避免阻塞；下轮 Flush 会重写全部快照。
                _logger.LogWarning(ex, "实时快照批量写入失败（{Count} 行，下轮重试）。", toWrite.Count);
            }
        }

        /// <summary>
        /// 单批多值 Upsert：INSERT ... ON DUPLICATE KEY UPDATE，参数化（无字符串拼接值）。
        /// 返回受影响行数（新增 1 / 更新 2），仅用于日志观测。
        /// </summary>
        private static async Task<int> UpsertBatchAsync(DbConnection conn, List<VariableRealtime> batch, CancellationToken token)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = BuildUpsertSql(batch.Count);

            for (var i = 0; i < batch.Count; i++)
            {
                var s = batch[i];
                var o = i * 8;
                AddParam(cmd, $"@p{o}", s.DeviceId);
                AddParam(cmd, $"@p{o + 1}", s.DeviceKey);
                AddParam(cmd, $"@p{o + 2}", s.VariableKey);
                AddParam(cmd, $"@p{o + 3}", s.VariableName);
                AddParam(cmd, $"@p{o + 4}", s.Value);
                AddParam(cmd, $"@p{o + 5}", s.RawValue);
                AddParam(cmd, $"@p{o + 6}", s.Quality);
                AddParam(cmd, $"@p{o + 7}", s.Timestamp);
            }

            return await cmd.ExecuteNonQueryAsync(token);
        }

        /// <summary>构造多值 upsert 语句（仅拼接参数占位符，不含任何数据值，杜绝注入）。</summary>
        private static string BuildUpsertSql(int count)
        {
            var sb = new StringBuilder();
            sb.Append("INSERT INTO VariableRealtime ");
            sb.Append("(DeviceId, DeviceKey, VariableKey, VariableName, `Value`, RawValue, Quality, Timestamp) VALUES ");

            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                var o = i * 8;
                sb.Append($"(@p{o},@p{o + 1},@p{o + 2},@p{o + 3},@p{o + 4},@p{o + 5},@p{o + 6},@p{o + 7})");
            }

            sb.Append(" ON DUPLICATE KEY UPDATE ");
            sb.Append("DeviceKey=VALUES(DeviceKey),VariableName=VALUES(VariableName),");
            sb.Append("`Value`=VALUES(`Value`),RawValue=VALUES(RawValue),");
            sb.Append("Quality=VALUES(Quality),Timestamp=VALUES(Timestamp)");
            return sb.ToString();
        }

        /// <summary>添加单个命令参数（null 转为 DBNull）。</summary>
        private static void AddParam(DbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static string BuildKey(int deviceId, string variableKey) => $"{deviceId}:{variableKey}";
    }
}
