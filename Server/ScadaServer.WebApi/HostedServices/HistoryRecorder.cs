using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 历史数据记录器（单例 + IHostedService）。
    /// <para>
    /// 采集线程通过 <see cref="IHistoryRecorder.Record"/> 非阻塞入队采样点，
    /// 本服务在后台按批次（满 100 条或每 500ms）批量写库，避免高频单条插入拖慢采集循环。
    /// 队列满时（BoundedChannelFullMode.DropWrite）丢弃并计数告警，保证采集不受背压阻塞。
    /// </para>
    /// <para>
    /// 落库前等待数据库初始化完成（与 RuntimeHostedService 同一套 DatabaseInitializationStatus 协调），
    /// 避免在迁移完成前对缺失表写入。
    /// </para>
    /// </summary>
    public class HistoryRecorder : IHistoryRecorder, IHostedService
    {
        private const int ChannelCapacity = 20000;
        private const int FlushBatchSize = 100;
        private const int FlushIntervalMs = 500;

        private readonly Channel<VariableHistory> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<HistoryRecorder> _logger;
        private readonly DatabaseInitializationStatus _dbReady;
        private readonly IInfluxStore _influxStore;
        private readonly CancellationTokenSource _cts = new();

        private Task? _processTask;
        private long _droppedCount;

        public HistoryRecorder(
            IServiceScopeFactory scopeFactory,
            ILogger<HistoryRecorder> logger,
            DatabaseInitializationStatus dbReady,
            IInfluxStore influxStore)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dbReady = dbReady;
            _influxStore = influxStore;
            _channel = Channel.CreateBounded<VariableHistory>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });
        }

        /// <inheritdoc/>
        public void Record(
            int deviceId,
            string deviceKey,
            string variableKey,
            string variableName,
            double value,
            string? rawValue,
            string? quality,
            DateTime sampleTime)
        {
            var point = new VariableHistory
            {
                DeviceId = deviceId,
                DeviceKey = deviceKey,
                VariableKey = variableKey,
                VariableName = variableName,
                Value = value,
                RawValue = rawValue,
                Timestamp = sampleTime,
                Quality = quality
            };

            if (!_channel.Writer.TryWrite(point))
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
            // 先关闭通道让后台排空剩余数据，再取消阻塞读，最后等待退出。
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
                    _logger.LogWarning("历史记录服务停止超时，剩余数据可能未完全落库。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "历史记录服务后台循环退出异常。");
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
                    _logger.LogWarning("数据库初始化未完成，历史记录服务退出（本次不写入）。");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var batch = new List<VariableHistory>(FlushBatchSize);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    VariableHistory item;
                    try
                    {
                        item = await _channel.Reader.ReadAsync(token);
                    }
                    catch (ChannelClosedException)
                    {
                        break; // 队列已关闭（优雅停止）
                    }

                    batch.Add(item);

                    // 顺带把已排队的项尽量捞进本批
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
                        // 等待累积窗口，把未满批次也按时落库
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
                _logger.LogError(ex, "历史记录服务后台循环因未预期异常退出。");
            }

            // 停止前排空剩余数据（不因取消而丢失）
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
                _logger.LogWarning(ex, "历史记录服务停止时刷新剩余数据失败。");
            }

            if (_droppedCount > 0)
            {
                _logger.LogWarning("历史记录队列满载丢弃 {Count} 条采样点。", Interlocked.Read(ref _droppedCount));
            }
        }

        private async Task FlushAsync(List<VariableHistory> batch, CancellationToken token)
        {
            if (batch.Count == 0) return;

            try
            {
                // 优先写 InfluxDB（已配置且写入成功）时不再落 MySQL，减少冗余。
                if (_influxStore.IsConfigured)
                {
                    try
                    {
                        var influxOk = await _influxStore.WriteAsync(batch);
                        if (influxOk)
                        {
                            _logger.LogDebug("已写入 InfluxDB {Count} 条历史记录。", batch.Count);
                            return;
                        }
                        _logger.LogWarning("InfluxDB 写入失败，回退写入 MySQL（{Count} 条）。", batch.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "InfluxDB 写入异常，回退写入 MySQL（{Count} 条）。", batch.Count);
                    }
                }

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                db.VariableHistories.AddRange(batch);
                await db.SaveChangesAsync(token);
                _logger.LogDebug("已批量写入 {Count} 条历史记录。", batch.Count);
            }
            catch (Exception ex)
            {
                // 写入失败不重试，避免阻塞采集；记录日志以便诊断。
                _logger.LogWarning(ex, "历史记录批量写入失败（丢弃 {Count} 条）。", batch.Count);
            }
            finally
            {
                batch.Clear();
            }
        }
    }
}
