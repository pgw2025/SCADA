using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 报警记录器（单例 + IHostedService）。
    /// <para>
    /// 运行时报警检测通过 <see cref="IAlarmRecorder.Record"/> 非阻塞入队报警事件，
    /// 本服务在后台按批次（满 100 条或每 500ms）批量写库，避免高频单条插入拖慢采集循环。
    /// 队列满时（BoundedChannelFullMode.DropWrite）丢弃并计数告警，保证采集不受背压阻塞。
    /// 落库前等待数据库初始化完成，避免在迁移完成前对缺失表写入。
    /// </para>
    /// </summary>
    public class AlarmRecorder : IAlarmRecorder, IHostedService
    {
        private const int ChannelCapacity = 10000;
        private const int FlushBatchSize = 100;
        private const int FlushIntervalMs = 500;

        private readonly Channel<AlarmEvent> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AlarmRecorder> _logger;
        private readonly DatabaseInitializationStatus _dbReady;
        private readonly CancellationTokenSource _cts = new();

        private Task? _processTask;
        private long _droppedCount;

        public AlarmRecorder(
            IServiceScopeFactory scopeFactory,
            ILogger<AlarmRecorder> logger,
            DatabaseInitializationStatus dbReady)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dbReady = dbReady;
            _channel = Channel.CreateBounded<AlarmEvent>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });
        }

        /// <inheritdoc/>
        public void Record(AlarmEvent evt)
        {
            if (!_channel.Writer.TryWrite(evt))
            {
                Interlocked.Increment(ref _droppedCount);
            }
        }

        /// <inheritdoc/>
        public void Complete() => _channel.Writer.TryComplete();

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
            Complete();
            _cts.Cancel();
            if (_processTask is not null)
            {
                try
                {
                    // 等待循环排空并完成最终落库；超时兜底防止宿主关闭被拖死。
                    await _processTask.WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("报警记录服务停止超时，剩余数据可能未完全落库。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "报警记录服务后台循环退出异常。");
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
                    _logger.LogWarning("数据库初始化未完成，报警记录服务退出（本次不写入）。");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var batch = new List<AlarmEvent>(FlushBatchSize);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    AlarmEvent item;
                    try
                    {
                        item = await _channel.Reader.ReadAsync(token);
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }

                    batch.Add(item);

                    while (batch.Count < FlushBatchSize && _channel.Reader.TryRead(out var next))
                    {
                        batch.Add(next);
                    }

                    if (batch.Count >= FlushBatchSize)
                    {
                        await FlushAsync(batch, token);
                    }
                    else
                    {
                        await Task.Delay(FlushIntervalMs, token);
                        if (batch.Count > 0)
                        {
                            await FlushAsync(batch, token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出路径
            }
            catch (Exception ex)
            {
                // 未预期异常不能让循环静默死亡（fire-and-forget 时代无法察觉），记录后继续走排空逻辑。
                _logger.LogError(ex, "报警记录服务后台循环因未预期异常退出。");
            }

            // 停止前排空剩余数据
            try
            {
                while (batch.Count < FlushBatchSize && _channel.Reader.TryRead(out var rest))
                {
                    batch.Add(rest);
                }
                if (batch.Count > 0)
                {
                    await FlushAsync(batch, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "报警记录服务停止时刷新剩余数据失败。");
            }

            if (_droppedCount > 0)
            {
                _logger.LogWarning("报警记录队列满载丢弃 {Count} 条事件。", Interlocked.Read(ref _droppedCount));
            }
        }

        private async Task FlushAsync(List<AlarmEvent> batch, CancellationToken token)
        {
            if (batch.Count == 0) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                var inserts = new List<AlarmRecord>();

                foreach (var evt in batch)
                {
                    if (evt.EventType == AlarmEventType.Recovered)
                    {
                        await MarkRecoveredAsync(db, evt, token);
                    }
                    else
                    {
                        inserts.Add(MapToEntity(evt));
                    }
                }

                if (inserts.Count > 0)
                {
                    db.AlarmRecords.AddRange(inserts);
                    await db.SaveChangesAsync(token);
                }
            }
            catch (Exception ex)
            {
                // 写入失败不重试，避免阻塞采集；记录日志以便诊断。
                _logger.LogWarning(ex, "报警记录批量写入失败（丢弃 {Count} 条事件）。", batch.Count);
            }
            finally
            {
                batch.Clear();
            }
        }

        /// <summary>
        /// 将恢复事件关联到"最新一条同键未恢复"记录并更新其恢复时间/值。
        /// </summary>
        private static async Task MarkRecoveredAsync(ScadaDbContext db, AlarmEvent evt, CancellationToken token)
        {
            var query = db.AlarmRecords
                .Where(r => r.DeviceId == evt.DeviceId
                            && r.VariableKey == evt.VariableKey
                            && r.RecoveredAt == null);

            // 规则告警严格按 RuleId 关联；兜底告警关联 RuleId 为空的记录。
            query = evt.RuleId.HasValue
                ? query.Where(r => r.RuleId == evt.RuleId.Value)
                : query.Where(r => r.RuleId == null);

            var record = await query
                .OrderByDescending(r => r.TriggeredAt)
                .FirstOrDefaultAsync(token);

            if (record != null)
            {
                record.RecoveredAt = evt.TriggeredAt;
                record.RecoveryValue = evt.ActualValue;
                await db.SaveChangesAsync(token);
            }
        }

        /// <summary>
        /// 触发事件映射为报警记录实体。
        /// </summary>
        private static AlarmRecord MapToEntity(AlarmEvent evt) => new()
        {
            DeviceId = evt.DeviceId,
            DeviceKey = evt.DeviceKey,
            VariableKey = evt.VariableKey,
            DataPointId = evt.DataPointId,
            VariableName = evt.VariableName,
            RuleId = evt.RuleId,
            RuleName = evt.RuleName,
            Level = evt.Level,
            Condition = evt.Condition,
            Threshold = evt.Threshold,
            ActualValue = evt.ActualValue,
            Message = evt.Message,
            Source = evt.Source,
            TriggeredAt = evt.TriggeredAt
        };
    }
}