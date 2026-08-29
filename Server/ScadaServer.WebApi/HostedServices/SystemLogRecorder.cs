using System.Threading;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.WebApi.Hubs;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 系统日志记录器（单例 + IHostedService）。
    /// <para>
    /// 承载两条写入链路：
    /// 1. 运行日志（<see cref="RecordRuntime"/>）：来自 ILogger 采集，高频、可丢弃——
    ///    有界通道 + DropWrite，队列满载时丢弃并计数告警，保证日志写入不阻塞业务线程。
    /// 2. 操作/安全日志（<see cref="RecordOperation"/>）：来自操作审计，低频、属审计凭据、不可丢——
    ///    无界通道，逐条入队，写库失败降级为 ILogger 告警（至少控制台留痕）。
    /// </para>
    /// <para>
    /// 后台按批次（满 100 条或每 500ms）批量落库；落库前等待数据库初始化完成
    /// （与 HistoryRecorder 同一套 DatabaseInitializationStatus 协调，避免迁移完成前对缺失表写入）。
    /// </para>
    /// <para>
    /// 落库后仅向 SystemLogHub 广播非敏感运行日志（Category=Runtime 且 Level≥Information），
    /// 不含操作人/IP 的操作/安全日志不推送，防止通过 SignalR 泄露敏感信息。
    /// </para>
    /// </summary>
    public class SystemLogRecorder : IHostedService
    {
        private const int ChannelCapacity = 10000;
        private const int FlushBatchSize = 100;
        private const int FlushIntervalMs = 500;

        private readonly Channel<SystemLog> _runtimeChannel;
        private readonly Channel<SystemLog> _operationChannel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SystemLogRecorder> _logger;
        private readonly DatabaseInitializationStatus _dbReady;
        private readonly IHubContext<SystemLogHub> _hubContext;
        private readonly CancellationTokenSource _cts = new();

        private Task? _processTask;
        private long _droppedCount;

        public SystemLogRecorder(
            IServiceScopeFactory scopeFactory,
            ILogger<SystemLogRecorder> logger,
            DatabaseInitializationStatus dbReady,
            IHubContext<SystemLogHub> hubContext)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dbReady = dbReady;
            _hubContext = hubContext;

            // 运行日志：有界 + 丢弃（高频、可接受丢失）
            _runtimeChannel = Channel.CreateBounded<SystemLog>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });

            // 操作/安全日志：无界（审计凭据，不丢弃）
            _operationChannel = Channel.CreateUnbounded<SystemLog>(new UnboundedChannelOptions
            {
                SingleReader = true
            });
        }

        /// <summary>
        /// 入队一条运行日志（Logger 采集调用，非阻塞）。
        /// </summary>
        public void RecordRuntime(string level, string source, string content)
        {
            var log = new SystemLog
            {
                Timestamp = DateTime.Now,
                Category = "Runtime",
                Level = level,
                Source = source,
                Content = content
            };

            if (!_runtimeChannel.Writer.TryWrite(log))
            {
                Interlocked.Increment(ref _droppedCount);
            }
        }

        /// <summary>
        /// 入队一条操作/安全日志（审计调用，非阻塞；无界队列不丢弃）。
        /// </summary>
        public void RecordOperation(
            string category,
            string level,
            string source,
            string? operation,
            string? operatorName,
            string? ipAddress,
            string? relatedId,
            string content)
        {
            var log = new SystemLog
            {
                Timestamp = DateTime.Now,
                Category = category,
                Level = level,
                Source = source,
                Operation = operation,
                Operator = operatorName,
                IpAddress = ipAddress,
                RelatedId = relatedId,
                Content = content
            };

            _operationChannel.Writer.TryWrite(log);
        }

        private void Complete()
        {
            _runtimeChannel.Writer.TryComplete();
            _operationChannel.Writer.TryComplete();
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
                    _logger.LogWarning("系统日志记录服务停止超时，剩余数据可能未完全落库。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "系统日志记录服务后台循环退出异常。");
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
                    _logger.LogWarning("数据库初始化未完成，系统日志记录服务退出（本次不写入）。");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var batch = new List<SystemLog>(FlushBatchSize);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // 等待运行日志信号；同时设置超时，保证运行日志空闲时操作日志仍能被及时处理。
                    var runtimeWait = _runtimeChannel.Reader.WaitToReadAsync(token).AsTask();
                    var delayTask = Task.Delay(FlushIntervalMs, token);
                    var winner = await Task.WhenAny(runtimeWait, delayTask);

                    if (winner == runtimeWait)
                    {
                        // 通道已关闭（优雅停止）→ 进入排空逻辑
                        if (!runtimeWait.Result)
                            break;
                    }

                    // 收集本批：运行日志尽量多读，操作日志全部排空
                    while (batch.Count < FlushBatchSize && _runtimeChannel.Reader.TryRead(out var r))
                    {
                        batch.Add(r);
                        BroadcastRuntimeIfNeeded(r);
                    }
                    while (_operationChannel.Reader.TryRead(out var o))
                    {
                        batch.Add(o);
                    }

                    if (batch.Count > 0)
                    {
                        await FlushAsync(batch, token);
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
                _logger.LogError(ex, "系统日志记录服务后台循环因未预期异常退出。");
            }

            // 停止前排空剩余数据（不因取消而丢失）
            try
            {
                while (batch.Count < FlushBatchSize && _runtimeChannel.Reader.TryRead(out var rest))
                {
                    batch.Add(rest);
                    BroadcastRuntimeIfNeeded(rest);
                }
                while (_operationChannel.Reader.TryRead(out var opRest))
                {
                    batch.Add(opRest);
                }
                if (batch.Count > 0)
                {
                    await FlushAsync(batch, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "系统日志记录服务停止时刷新剩余数据失败。");
            }

            if (_droppedCount > 0)
            {
                _logger.LogWarning("系统日志运行队列满载丢弃 {Count} 条。", Interlocked.Read(ref _droppedCount));
            }
        }

        /// <summary>
        /// 广播非敏感运行日志（仅 Runtime 分类且 Level≥Information；操作/安全日志含敏感字段不推送）。
        /// </summary>
        private void BroadcastRuntimeIfNeeded(SystemLog log)
        {
            if (log.Category != "Runtime")
                return;
            if (log.Level is not ("Information" or "Warning" or "Error" or "Critical"))
                return;

            // 不推送到自身：广播失败不影响日志记录（吞异常）。
            _ = _hubContext.Clients.All.SendAsync("ReceiveLog", new SystemLogDto
            {
                Id = 0,
                Timestamp = log.Timestamp,
                Category = log.Category,
                Level = log.Level,
                Source = log.Source,
                Content = log.Content
            });
        }

        private async Task FlushAsync(List<SystemLog> batch, CancellationToken token)
        {
            if (batch.Count == 0) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                db.SystemLogs.AddRange(batch);
                await db.SaveChangesAsync(token);
                _logger.LogDebug("已批量写入 {Count} 条系统日志。", batch.Count);
            }
            catch (OperationCanceledException)
            {
                // 主循环在应用关闭时用同样的 token 取消，落库中途取消属预期的正常退出路径，
                // 此时由排空逻辑以 CancellationToken.None 做最终写入，这里不作为错误记录。
                _logger.LogDebug("系统日志批量写入被关闭流程取消（丢弃 {Count} 条）。", batch.Count);
            }
            catch (Exception ex)
            {
                // 运行日志写入失败可接受（不重试，避免阻塞）；操作日志失败降级为 ILogger 告警，至少控制台留痕。
                _logger.LogError(ex, "系统日志批量写入失败（丢弃 {Count} 条）。", batch.Count);
            }
            finally
            {
                batch.Clear();
            }
        }
    }
}
