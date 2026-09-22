using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 历史数据记录器（单例 + IHostedService）。
    /// <para>
    /// 采集线程通过 <see cref="IHistoryRecorder.Record"/> 非阻塞入队采样点，
    /// 本服务在后台按批次（满 FlushBatchSize 条或每 FlushIntervalMs）批量写库，避免高频单条插入拖慢采集循环。
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
    /// <para>
    /// 补偿落盘（WAL，阶段2增强）：内存补偿队列溢出、或进程停止时仍滞留的批次暂存磁盘
    /// （HistoryRecorderOptions.RetrySpillDirectory），进程重启后扫描重放，避免双后端故障期间
    /// 的历史数据随进程退出丢失。批次以 JSON Lines 单文件一个批次存储，写盘经 .tmp 原子 rename。
    /// </para>
    /// </summary>
    public class HistoryRecorder : IHistoryRecorder, IHistoryRecorderStats, IHostedService
    {
        private const long DefaultSpillMaxBytes = 512L * 1024 * 1024;

        /// <summary>停止排空预算（毫秒）：停止时刷空队列的整体时限，防止数据库持续故障拖死关闭流程。</summary>
        private const int StopDrainTimeoutMs = 25000;

        /// <summary>主队列满载丢弃的日志闸门跨度（条）：首次丢弃记 Warning，之后每累积该跨度再记一次。</summary>
        private const int DropNotifyInterval = 1000;

        /// <summary>MySQL 回退写入重试延迟序列（毫秒）：500/1000/2000。</summary>
        private static readonly int[] MySqlRetryDelays = { 500, 1000, 2000 };

        private readonly Channel<VariableHistory> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<HistoryRecorder> _logger;
        private readonly DatabaseInitializationStatus _dbReady;
        private readonly IInfluxStore _influxStore;
        private readonly CancellationTokenSource _cts = new();

        // ---- 采集/落库参数（来自 HistoryRecorderOptions） ----
        private readonly int _channelCapacity;
        private readonly int _flushBatchSize;
        private readonly int _flushIntervalMs;
        private readonly int _retryBufferCapacity;
        private readonly int _maxRetryRounds;

        // ---- 补偿落盘（WAL） ----
        private readonly bool _spillEnabled;
        private readonly string? _spillDir;
        private readonly long _spillMaxBytes;

        private Task? _processTask;

        // ---- 运行期统计（Interlocked 零锁读取） ----
        private long _droppedCount;                 // 队列满丢弃
        private long _dropNotifiedCount;            // 已告警的累计丢弃数（日志闸门用）
        private long _enqueuedTotal;                // 累计入队
        private long _droppedAllBackendFailed;      // 双后端穷尽 + 补偿溢出丢弃 + 落盘失败丢弃
        private long _influxWriteBatches;           // Influx 成功批次数
        private long _influxWriteFailedBatches;     // Influx 失败批次数
        private long _mysqlWriteBatches;            // MySQL 成功批次数
        private long _mysqlRetriedBatches;          // MySQL 重试批次数
        private long _invalidValuePoints;           // NaN/Infinity 分流计数
        private int _maxQueueDepth;                 // 队列深度高水位（评估容量）
        private double _lastFlushDurationMs;        // 最近一次落库耗时（毫秒）
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
            IInfluxStore influxStore,
            IOptions<HistoryRecorderOptions> options,
            IWebHostEnvironment environment)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dbReady = dbReady;
            _influxStore = influxStore;

            var opt = options.Value ?? new HistoryRecorderOptions();
            _channelCapacity = opt.ChannelCapacity > 0 ? opt.ChannelCapacity : 20000;
            _flushBatchSize = opt.FlushBatchSize > 0 ? opt.FlushBatchSize : 100;
            _flushIntervalMs = opt.FlushIntervalMs > 0 ? opt.FlushIntervalMs : 500;
            _retryBufferCapacity = opt.RetryBufferCapacity > 0 ? opt.RetryBufferCapacity : 50000;
            _maxRetryRounds = opt.MaxRetryRounds > 0 ? opt.MaxRetryRounds : 10;

            // 落盘目录：RetrySpillDirectory 可绝对或相对 ContentRoot；空或未启用则为 null（不落盘）。
            var spillEnabled = opt.RetrySpillEnabled;
            var spillRel = opt.RetrySpillDirectory ?? string.Empty;
            _spillEnabled = spillEnabled && !string.IsNullOrWhiteSpace(spillRel);
            _spillDir = _spillEnabled
                ? (Path.IsPathRooted(spillRel)
                    ? spillRel
                    : Path.Combine(environment.ContentRootPath, spillRel))
                : null;
            _spillMaxBytes = opt.RetrySpillMaxBytes > 0 ? opt.RetrySpillMaxBytes : DefaultSpillMaxBytes;

            _channel = Channel.CreateBounded<VariableHistory>(new BoundedChannelOptions(_channelCapacity)
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
                // 运行期丢弃告警（日志闸门）：首次丢弃记 Warning，之后每累积 DropNotifyInterval 条再记一次，
                // 避免高频满载期间每一条都刷日志（原实现仅在进程停止时才打一条总告警，运行期丢数无感知）。
                var dropped = Interlocked.Increment(ref _droppedCount);
                var notified = Interlocked.Read(ref _dropNotifiedCount);
                if (dropped == 1 || dropped - notified >= DropNotifyInterval)
                {
                    Interlocked.Exchange(ref _dropNotifiedCount, dropped);
                    _logger.LogWarning(
                        "历史记录主队列满载丢弃（累计 {Dropped} 条）。请排查 Influx/MySQL 写入情况或调大队列容量。",
                        dropped);
                }
            }
            else
            {
                // 记录队列深度高水位（评估容量是否需要调大，随 GetStats 暴露）
                var depth = _channel.Reader.Count;
                var currentMax = Volatile.Read(ref _maxQueueDepth);
                while (depth > currentMax)
                {
                    if (Interlocked.CompareExchange(ref _maxQueueDepth, depth, currentMax) == currentMax)
                    {
                        break;
                    }
                    currentMax = Volatile.Read(ref _maxQueueDepth);
                }
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
                MaxQueueDepth = Volatile.Read(ref _maxQueueDepth),
                LastFlushDurationMs = Volatile.Read(ref _lastFlushDurationMs),
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

            // 停止阶段：把仍滞留内存补偿队列的批次落盘（WAL），保证进程退出后不随内存丢失；
            // 下次启动 ReplaySpillDirectoryAsync 会补写。
            DrainRetryBufferToSpill();
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

            var batch = new List<VariableHistory>(_flushBatchSize);

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
                    while (batch.Count < _flushBatchSize && _channel.Reader.TryRead(out var next))
                    {
                        batch.Add(next);
                    }

                    if (batch.Count >= _flushBatchSize)
                    {
                        await FlushAsync(batch, token);
                    }
                    else
                    {
                        // 等待累积窗口，把未满批次也按时落库
                        await Task.Delay(_flushIntervalMs, token);
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

            // 停止前排空剩余数据（不因取消而丢失）：
            // 循环逐个捞取队列剩余项，凑满一批即落库，直到队列排空——
            // 修复原先只刷一次（上限 FlushBatchSize）导致积压超一批时其余被静默丢弃的问题。
            // 整体受停止排空预算约束，防止数据库持续故障时长期占用关闭流程。
            try
            {
                using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                drainCts.CancelAfter(StopDrainTimeoutMs);
                try
                {
                    while (_channel.Reader.TryRead(out var rest))
                    {
                        batch.Add(rest);
                        if (batch.Count >= _flushBatchSize)
                        {
                            await FlushAsync(batch, drainCts.Token);
                        }
                    }
                    if (batch.Count > 0)
                    {
                        await FlushAsync(batch, drainCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "历史记录服务停止排空超时（>{TimeoutMs}ms），剩余数据可能未完全落库。",
                        StopDrainTimeoutMs);
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

            // SF-3 批内去重：按 (DeviceId, VariableKey, Timestamp) 保留首条，防批内自重复引发整批异常。
            DeduplicateBatch(batch);
            if (batch.Count == 0) return;

            _lastFlushAt = DateTime.UtcNow;
            var sw = Stopwatch.StartNew();

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
                _lastFlushDurationMs = sw.Elapsed.TotalMilliseconds;
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

        /// <summary>
        /// 批内去重（SF-3）：按 (DeviceId, VariableKey, Timestamp) 保留首条，删除后续重复项，
        /// 防止同一批内自重复触发唯一索引冲突整批失败。
        /// </summary>
        private static void DeduplicateBatch(List<VariableHistory> batch)
        {
            if (batch.Count < 2)
            {
                return;
            }

            var seen = new HashSet<(int DeviceId, string VariableKey, DateTime Timestamp)>();
            batch.RemoveAll(p => !seen.Add((p.DeviceId, p.VariableKey, p.Timestamp)));
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
                    var repo = scope.ServiceProvider.GetRequiredService<IVariableHistoryRepository>();
                    await repo.InsertIdempotentAsync(points, token);

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

        /// <summary>补偿批次入队；内存溢出时落盘（WAL），落盘失败才丢弃并计数。</summary>
        private void EnqueueRetryBatch(List<VariableHistory> points)
        {
            lock (_retryLock)
            {
                if (_retryBufferCount + points.Count > _retryBufferCapacity)
                {
                    // 内存补偿队列满：优先落盘暂存，避免数据直接丢弃。
                    if (TrySpillBatch(points))
                    {
                        return;
                    }

                    Interlocked.Add(ref _droppedAllBackendFailed, points.Count);
                    _logger.LogWarning(
                        "补偿队列溢出且落盘失败，丢弃 {Count} 条采样点。",
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
            LinkedListNode<RetryBatch>? headNode = null;
            lock (_retryLock)
            {
                headNode = _retryBuffer.First;
                head = headNode?.Value;
            }

            if (head == null || headNode == null)
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
                    var repo = scope.ServiceProvider.GetRequiredService<IVariableHistoryRepository>();
                    await repo.InsertIdempotentAsync(head.Points, token);
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

            if (success)
            {
                // 成功：出队
                lock (_retryLock)
                {
                    if (headNode.List != null)
                    {
                        _retryBuffer.Remove(headNode);
                        _retryBufferCount -= head.Points.Count;
                    }
                }
                _lastWriteSucceededAt = DateTime.UtcNow;
                _logger.LogInformation("补偿批次重放成功（{Count} 条）。", head.Points.Count);
                return;
            }

            // 失败：毒丸隔离
            head.FailedRounds++;
            if (head.FailedRounds >= _maxRetryRounds)
            {
                // 连续失败达到上限：移出队列并落盘暂存（WAL），避免毒丸批次永久占用队头导致数据硬丢失；
                // 落盘失败才丢弃并计数（下次启动 ReplaySpillDirectoryAsync 会补写）。
                lock (_retryLock)
                {
                    if (headNode.List != null)
                    {
                        _retryBuffer.Remove(headNode);
                        _retryBufferCount -= head.Points.Count;
                    }
                }
                if (TrySpillBatch(head.Points))
                {
                    _logger.LogWarning(
                        "补偿批次连续失败 {Rounds} 轮，已落盘暂存 {Count} 条。",
                        _maxRetryRounds,
                        head.Points.Count);
                }
                else
                {
                    Interlocked.Add(ref _droppedAllBackendFailed, head.Points.Count);
                    _logger.LogWarning(
                        "补偿批次连续失败 {Rounds} 轮，落盘失败，丢弃 {Count} 条。",
                        _maxRetryRounds,
                        head.Points.Count);
                }
                return;
            }

            // 未到放弃轮数：把当前批次轮转到队尾（round-robin），
            // 避免毒丸队头长期阻塞其后正常批次（每个失败批次轮流重试、各自计轮）。
            lock (_retryLock)
            {
                if (headNode.List != null)
                {
                    _retryBuffer.Remove(headNode);
                    _retryBuffer.AddLast(headNode);
                }
            }
        }

        // ===================== 补偿落盘（WAL） =====================

        /// <summary>把补偿批次落盘暂存（单文件一个批次，JSON 数组；写 .tmp 后原子 rename 防半文件）。</summary>
        private bool TrySpillBatch(IReadOnlyList<VariableHistory> points)
        {
            if (!_spillEnabled || string.IsNullOrEmpty(_spillDir))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(_spillDir);
                var file = Path.Combine(
                    _spillDir,
                    $"retry_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json");
                var tmp = file + ".tmp";
                var json = JsonSerializer.Serialize(points);
                File.WriteAllText(tmp, json);
                File.Move(tmp, file); // 原子 rename：重放扫描时不会读到半文件

                EnforceSpillBudget();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "历史补偿批次落盘失败（{Count} 条）。", points.Count);
                return false;
            }
        }

        /// <summary>落盘目录预算：总大小超上限时从最旧文件开始清理，防止磁盘无限增长。</summary>
        private void EnforceSpillBudget()
        {
            if (string.IsNullOrEmpty(_spillDir))
            {
                return;
            }

            try
            {
                var files = Directory.GetFiles(_spillDir, "retry_*.json")
                    .Select(f => new FileInfo(f))
                    .OrderBy(fi => fi.LastWriteTimeUtc)
                    .ToList();

                var total = 0L;
                foreach (var fi in files)
                {
                    total += fi.Length;
                }

                foreach (var fi in files)
                {
                    if (total <= _spillMaxBytes)
                    {
                        break;
                    }
                    total -= fi.Length;
                    try
                    {
                        File.Delete(fi.FullName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "清理历史补偿落盘文件失败：{File}", fi.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "历史补偿落盘预算检查失败。");
            }
        }

        /// <summary>
        /// 停止阶段：把仍滞留内存补偿队列的批次全部落盘并清空内存，保证进程退出后不丢。
        /// 落盘失败的批次只能丢弃（进程即将退出，无更可靠去处）。
        /// </summary>
        private void DrainRetryBufferToSpill()
        {
            if (!_spillEnabled)
            {
                return;
            }

            List<RetryBatch> toSpill;
            lock (_retryLock)
            {
                if (_retryBuffer.Count == 0)
                {
                    return;
                }
                toSpill = _retryBuffer.ToList();
                _retryBuffer.Clear();
                _retryBufferCount = 0;
            }

            foreach (var b in toSpill)
            {
                if (!TrySpillBatch(b.Points))
                {
                    Interlocked.Add(ref _droppedAllBackendFailed, b.Points.Count);
                    _logger.LogWarning("停止时补偿批次落盘失败，丢弃 {Count} 条。", b.Points.Count);
                }
            }
        }

        /// <summary>
        /// 扫描历史补偿落盘目录并重放：后端恢复后把上次滞留的批次补写，成功后删除文件。
        /// 遇到后端仍故障的批次则停止扫描（文件保留，下一轮再试），避免每轮重试所有文件。
        /// </summary>
        private async Task ReplaySpillDirectoryAsync(CancellationToken token)
        {
            if (!_spillEnabled || string.IsNullOrEmpty(_spillDir) || !Directory.Exists(_spillDir))
            {
                return;
            }

            string[] files;
            try
            {
                // 文件名带时间戳前缀，字典序即写入顺序
                files = Directory.GetFiles(_spillDir, "retry_*.json").OrderBy(f => f).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "历史补偿落盘目录扫描失败。");
                return;
            }

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();

                List<VariableHistory>? batch;
                try
                {
                    batch = JsonSerializer.Deserialize<List<VariableHistory>>(File.ReadAllText(file));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "历史补偿落盘文件解析失败，删除：{File}", file);
                    TryDeleteSpillFile(file);
                    continue;
                }

                if (batch == null || batch.Count == 0)
                {
                    TryDeleteSpillFile(file);
                    continue;
                }

                var ok = false;
                try
                {
                    ok = await TryWriteInfluxAsync(batch, token);
                    if (!ok)
                    {
                        ok = await TryWriteMySqlOnce(batch, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    return; // 退出扫描，文件保留，下次再试
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "历史补偿落盘重放异常，留待下一轮。");
                }

                if (!ok)
                {
                    // 后端仍故障：本批次保留，停止扫描，下一轮再试
                    return;
                }

                TryDeleteSpillFile(file);
                _lastWriteSucceededAt = DateTime.UtcNow;
                _logger.LogInformation("历史补偿落盘批次重放成功（{Count} 条）。", batch.Count);
            }
        }

        /// <summary>MySQL 单次写入（供落盘重放使用，不在此处耗尽重试）。</summary>
        private async Task<bool> TryWriteMySqlOnce(List<VariableHistory> points, CancellationToken token)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IVariableHistoryRepository>();
                await repo.InsertIdempotentAsync(points, token);
                Interlocked.Increment(ref _mysqlWriteBatches);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "历史补偿 MySQL 单次写入失败。");
                return false;
            }
        }

        /// <summary>删除历史补偿落盘文件（失败不抛出）。</summary>
        private void TryDeleteSpillFile(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除历史补偿落盘文件失败：{File}", file);
            }
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
