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
    /// <para>
    /// 写入可靠性（阶段2）：Influx 失败回退 MySQL，MySQL 指数退避重试；双后端均失败的批次进入
    /// 内存补偿队列周期性重放；NaN/Infinity 采样点剥离改道 MySQL，避免整批 Influx 写入失败。
    /// </para>
    /// </summary>
    public class HistoryRecorder : IHistoryRecorder, IHistoryRecorderStats, IHostedService
    {
        private const int ChannelCapacity = 20000;
        private const int FlushBatchSize = 100;
        private const int FlushIntervalMs = 500;

        /// <summary>补偿队列容量上限（条）。</summary>
        private const int RetryBufferCapacity = 50000;

        /// <summary>补偿批次连续失败放弃轮数上限（防毒丸批次永久占用）。</summary>
        private const int MaxRetryRounds = 10;

        /// <summary>MySQL 回退写入重试延迟序列（毫秒）：500/1000/2000。</summary>
        private static readonly int[] MySqlRetryDelays = { 500, 1000, 2000 };

        private readonly Channel<VariableHistory> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<HistoryRecorder> _logger;
        private readonly DatabaseInitializationStatus _dbReady;
        private readonly IInfluxStore _influxStore;
        private readonly CancellationTokenSource _cts = new();

        private Task? _processTask;

        // ---- 运行期统计（Interlocked 零锁读取） ----
        private long _droppedCount;                 // 队列满丢弃
        private long _enqueuedTotal;                // 累计入队
        private long _droppedAllBackendFailed;      // 双后端穷尽 + 补偿溢出丢弃
        private long _influxWriteBatches;           // Influx 成功批次数
        private long _influxWriteFailedBatches;     // Influx 失败批次数
        private long _mysqlWriteBatches;            // MySQL 成功批次数
        private long _mysqlRetriedBatches;          // MySQL 重试批次数
        private long _invalidValuePoints;           // NaN/Infinity 分流计数
        private DateTime? _lastFlushAt;
        private DateTime? _lastWriteSucceededAt;

        // ---- 补偿队列 ----
        private readonly object _retryLock = new();
        private readonly LinkedList<RetryBatch> _retryBuffer = new();
        private int _retryBufferCount;              // 补偿队列条数（受 _retryLock 保护）

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

            Interlocked.Increment(ref _enqueuedTotal);

            if (!_channel.Writer.TryWrite(point))
            {
                Interlocked.Increment(ref _droppedCount);
            }
        }

        /// <inheritdoc/>
        public void Complete() => _channel.Writer.TryComplete();

        /// <inheritdoc/>
        public HistoryRecorderStats GetStats()
        {
            int retryDepth;
            lock (_retryLock)
            {
                retryDepth = _retryBufferCount;
            }

            return new HistoryRecorderStats
            {
                QueueDepth = _channel.Reader.Count,
                EnqueuedTotal = Interlocked.Read(ref _enqueuedTotal),
                DroppedQueueFull = Interlocked.Read(ref _droppedCount),
                DroppedAllBackendFailed = Interlocked.Read(ref _droppedAllBackendFailed),
                InfluxWriteBatches = Interlocked.Read(ref _influxWriteBatches),
                InfluxWriteFailedBatches = Interlocked.Read(ref _influxWriteFailedBatches),
                MysqlWriteBatches = Interlocked.Read(ref _mysqlWriteBatches),
                MysqlRetriedBatches = Interlocked.Read(ref _mysqlRetriedBatches),
                InvalidValuePoints = Interlocked.Read(ref _invalidValuePoints),
                RetryBufferDepth = retryDepth,
                LastFlushAt = _lastFlushAt,
                LastWriteSucceededAt = _lastWriteSucceededAt
            };
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

                    // 补偿队列重放：主循环空闲窗口检查并重放失败批次
                    await ReplayRetryBufferAsync(token);
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

        /// <summary>
        /// 批量落库：Influx 优先（NaN/Infinity 剥离），失败回退 MySQL（指数退避重试），
        /// 双后端均失败进入补偿队列。
        /// </summary>
        private async Task FlushAsync(List<VariableHistory> batch, CancellationToken token)
        {
            if (batch.Count == 0) return;

            _lastFlushAt = DateTime.UtcNow;

            try
            {
                // 剥离 NaN/Infinity 采样点：改道 MySQL（Influx 行协议不接受非法浮点，避免整批失败）。
                List<VariableHistory> invalidPoints = null!;
                List<VariableHistory> normalPoints = batch;

                var invalidCount = CountInvalidPoints(batch);
                if (invalidCount > 0)
                {
                    invalidPoints = new List<VariableHistory>(invalidCount);
                    normalPoints = new List<VariableHistory>(batch.Count - invalidCount);
                    foreach (var p in batch)
                    {
                        if (double.IsNaN(p.Value) || double.IsInfinity(p.Value))
                        {
                            invalidPoints.Add(p);
                        }
                        else
                        {
                            normalPoints.Add(p);
                        }
                    }
                    Interlocked.Add(ref _invalidValuePoints, invalidCount);
                }

                // 正常点走 Influx 优先链路
                if (normalPoints.Count > 0)
                {
                    if (await TryWriteInfluxAsync(normalPoints, token))
                    {
                        _lastWriteSucceededAt = DateTime.UtcNow;
                        // 非法点改道 MySQL
                        if (invalidPoints != null && invalidPoints.Count > 0)
                        {
                            await WriteMySqlWithRetryAsync(invalidPoints, token);
                        }
                        return;
                    }

                    // Influx 失败 → 正常点 + 非法点合并回退 MySQL
                    if (invalidPoints != null && invalidPoints.Count > 0)
                    {
                        normalPoints.AddRange(invalidPoints);
                    }
                    await WriteMySqlWithRetryAsync(normalPoints, token);
                    return;
                }

                // 整批均为非法点：直接写 MySQL
                await WriteMySqlWithRetryAsync(batch, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "历史记录批量写入异常（{Count} 条）。", batch.Count);
            }
            finally
            {
                batch.Clear();
            }
        }

        /// <summary>统计批次中 NaN/Infinity 点数。</summary>
        private static int CountInvalidPoints(List<VariableHistory> batch)
        {
            var count = 0;
            foreach (var p in batch)
            {
                if (double.IsNaN(p.Value) || double.IsInfinity(p.Value))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Influx 优先写入；未配置或失败返回 false（调用方回退 MySQL）。</summary>
        private async Task<bool> TryWriteInfluxAsync(List<VariableHistory> points, CancellationToken token)
        {
            if (!_influxStore.IsConfigured)
            {
                return false;
            }

            try
            {
                var ok = await _influxStore.WriteAsync(points);
                if (ok)
                {
                    Interlocked.Increment(ref _influxWriteBatches);
                    _logger.LogDebug("已写入 InfluxDB {Count} 条历史记录。", points.Count);
                    return true;
                }

                Interlocked.Increment(ref _influxWriteFailedBatches);
                _logger.LogWarning("InfluxDB 写入失败，回退写入 MySQL（{Count} 条）。", points.Count);
                return false;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _influxWriteFailedBatches);
                _logger.LogWarning(ex, "InfluxDB 写入异常，回退写入 MySQL（{Count} 条）。", points.Count);
                return false;
            }
        }

        /// <summary>MySQL 写入（指数退避重试），重试穷尽后进入补偿队列。</summary>
        private async Task WriteMySqlWithRetryAsync(List<VariableHistory> points, CancellationToken token)
        {
            var attempt = 0;
            while (true)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                    db.VariableHistories.AddRange(points);
                    await db.SaveChangesAsync(token);

                    Interlocked.Increment(ref _mysqlWriteBatches);
                    if (attempt > 0)
                    {
                        Interlocked.Increment(ref _mysqlRetriedBatches);
                    }
                    _lastWriteSucceededAt = DateTime.UtcNow;
                    _logger.LogDebug("已批量写入 {Count} 条历史记录（MySQL）。", points.Count);
                    return;
                }
                catch (OperationCanceledException)
                {
                    // 应用关闭：直接进入补偿路径（停止排空阶段传入 None，不会走此分支）
                    EnqueueRetryBatch(points);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt < MySqlRetryDelays.Length)
                    {
                        attempt++;
                        Interlocked.Increment(ref _mysqlRetriedBatches);
                        _logger.LogWarning(
                            ex,
                            "MySQL 批量写入失败（第 {Attempt} 次重试，共 {Count} 条）。",
                            attempt,
                            points.Count);

                        try
                        {
                            await Task.Delay(MySqlRetryDelays[attempt - 1], token);
                        }
                        catch (OperationCanceledException)
                        {
                            EnqueueRetryBatch(points);
                            return;
                        }
                        continue;
                    }

                    // 重试穷尽：进入补偿队列
                    _logger.LogWarning(
                        ex,
                        "MySQL 批量写入重试穷尽，批次进入补偿队列（{Count} 条）。",
                        points.Count);
                    EnqueueRetryBatch(points);
                    return;
                }
            }
        }

        /// <summary>补偿批次入队；溢出则丢弃并计数。</summary>
        private void EnqueueRetryBatch(List<VariableHistory> points)
        {
            lock (_retryLock)
            {
                if (_retryBufferCount + points.Count > RetryBufferCapacity)
                {
                    Interlocked.Add(ref _droppedAllBackendFailed, points.Count);
                    _logger.LogWarning(
                        "补偿队列溢出，丢弃 {Count} 条采样点。",
                        points.Count);
                    return;
                }

                _retryBuffer.AddLast(new RetryBatch(points));
                _retryBufferCount += points.Count;
            }
        }

        /// <summary>周期性重放补偿队列（Influx 优先，恢复后出队；毒丸批次超轮放弃）。</summary>
        private async Task ReplayRetryBufferAsync(CancellationToken token)
        {
            RetryBatch? head = null;
            lock (_retryLock)
            {
                var node = _retryBuffer.First;
                if (node != null)
                {
                    head = node.Value;
                }
            }

            if (head == null)
            {
                return;
            }

            var success = false;
            try
            {
                // 优先尝试 Influx（当前已配置）
                success = await TryWriteInfluxAsync(head.Points, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                success = false;
            }

            if (!success)
            {
                try
                {
                    // 回退 MySQL（单次尝试，不在此处耗尽重试，留给下一轮）
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                    db.VariableHistories.AddRange(head.Points);
                    await db.SaveChangesAsync(token);
                    Interlocked.Increment(ref _mysqlWriteBatches);
                    success = true;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "补偿批次重放失败，留待下一轮。");
                }
            }

            if (!success)
            {
                head.FailedRounds++;
                if (head.FailedRounds >= MaxRetryRounds)
                {
                    // 毒丸批次：放弃并计数
                    lock (_retryLock)
                    {
                        var node = _retryBuffer.First;
                        if (node != null && ReferenceEquals(node.Value, head))
                        {
                            _retryBuffer.RemoveFirst();
                            _retryBufferCount -= head.Points.Count;
                        }
                    }
                    Interlocked.Add(ref _droppedAllBackendFailed, head.Points.Count);
                    _logger.LogWarning(
                        "补偿批次连续失败 {Rounds} 轮，放弃并丢弃 {Count} 条。",
                        MaxRetryRounds,
                        head.Points.Count);
                }
                return;
            }

            // 成功：出队
            lock (_retryLock)
            {
                var node = _retryBuffer.First;
                if (node != null && ReferenceEquals(node.Value, head))
                {
                    _retryBuffer.RemoveFirst();
                    _retryBufferCount -= head.Points.Count;
                }
            }
            _lastWriteSucceededAt = DateTime.UtcNow;
            _logger.LogInformation("补偿批次重放成功（{Count} 条）。", head.Points.Count);
        }

        /// <summary>补偿批次（持有采样点与失败轮数）。</summary>
        private sealed class RetryBatch
        {
            public RetryBatch(List<VariableHistory> points)
            {
                Points = points;
            }

            public List<VariableHistory> Points { get; }

            public int FailedRounds { get; set; }
        }
    }
}
