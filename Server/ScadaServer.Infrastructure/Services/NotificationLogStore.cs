using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
using ScadaServer.Domain.Exceptions;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Services
{
    /// <summary>
    /// 投递记录存储：写入器（IHostedService 后台消费循环批量落库）+ 查询/清空/重试服务。
    /// <para>
    /// Recorder：Record 仅向有界 Channel（DropWrite）写入后立即返回，绝不阻塞/抛出到发送循环
    /// （采集路径安全红线）；消费循环每批开一个 Scope 统一落库；SourceLogId 非空走 UPDATE 回写原行（重试路径）。
    /// 启动时清扫上次运行中断的在途重试行（Retrying → Failed），避免僵尸过渡态。
    /// </para>
    /// </summary>
    public class NotificationLogRecorder : INotificationLogRecorder, IHostedService
    {
        /// <summary>内部待写队列容量（满载 DropWrite：记录完整性让位于发送主流程）。</summary>
        private const int QueueCapacity = 1024;

        /// <summary>单批落库上限。</summary>
        private const int BatchSize = 64;

        /// <summary>停机排空超时（批量落库是短事务，5s 足够）。</summary>
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NotificationLogRecorder> _logger;
        private readonly Channel<NotificationLogEntry> _channel;
        private readonly CancellationTokenSource _cts = new();
        private Task? _consumeTask;
        private long _droppedCount;

        public NotificationLogRecorder(IServiceScopeFactory scopeFactory, ILogger<NotificationLogRecorder> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _channel = Channel.CreateBounded<NotificationLogEntry>(new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });
        }

        /// <inheritdoc/>
        public void Record(NotificationLogEntry entry)
        {
            if (!_channel.Writer.TryWrite(entry))
            {
                Interlocked.Increment(ref _droppedCount);
            }
        }

        /// <inheritdoc/>
        public void DiscardPending()
        {
            var discarded = 0;
            while (_channel.Reader.TryRead(out _))
            {
                discarded++;
            }
            if (discarded > 0)
            {
                _logger.LogInformation("已丢弃 {Count} 条待写投递记录（配合清空操作）。", discarded);
            }
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // R4 启动清扫：上次运行中断的在途重试统一置回失败，避免行永久停留 Retrying 过渡态。
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                var recovered = await db.NotificationLogs
                    .Where(l => l.Status == "Retrying")
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.Status, "Failed")
                        .SetProperty(l => l.Error, "服务重启，重试中断")
                        .SetProperty(l => l.TimestampUtc, DateTime.UtcNow), cancellationToken);
                if (recovered > 0)
                {
                    _logger.LogWarning("启动清扫：{Count} 条在途重试记录已置为 Failed。", recovered);
                }
            }
            catch (Exception ex)
            {
                // 清扫失败不阻断宿主启动，消费循环照常运行
                _logger.LogWarning(ex, "投递记录启动清扫失败，将随下次启动重试。");
            }

            _consumeTask = ConsumeAsync(_cts.Token);
            _logger.LogInformation("投递记录写入器已启动。");
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete();
            if (_consumeTask is not null)
            {
                try
                {
                    await _consumeTask.WaitAsync(DrainTimeout, CancellationToken.None);
                }
                catch (TimeoutException)
                {
                    _cts.Cancel();
                    _logger.LogWarning("投递记录消费循环排空超时，已强制取消（剩余记录丢弃）。");
                }
            }

            var dropped = Interlocked.Read(ref _droppedCount);
            if (dropped > 0)
            {
                _logger.LogWarning("投递记录待写队列满载丢弃 {Count} 条。", dropped);
            }
        }

        /// <summary>后台消费循环：批取出 → 每批一个 Scope 统一落库（UPDATE 回写优先于 INSERT）。</summary>
        private async Task ConsumeAsync(CancellationToken token)
        {
            try
            {
                while (await _channel.Reader.WaitToReadAsync(token))
                {
                    var batch = new List<NotificationLogEntry>(BatchSize);
                    while (batch.Count < BatchSize && _channel.Reader.TryRead(out var entry))
                    {
                        batch.Add(entry);
                    }
                    if (batch.Count == 0) continue;

                    try
                    {
                        await FlushAsync(batch, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        // 记录失败不影响发送主流程：仅记日志（本服务异常不进入任何外发挂钩）
                        _logger.LogError(ex, "投递记录批量落库失败（本批 {Count} 条丢弃）。", batch.Count);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 停机兜底取消：正常退出
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "投递记录消费循环异常退出。");
            }
        }

        /// <summary>单批落库：SourceLogId 非空 → ExecuteUpdate 回写原行（容忍 0 行命中）；否则新增。</summary>
        private async Task FlushAsync(List<NotificationLogEntry> batch, CancellationToken token)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

            var updates = batch.Where(b => b.SourceLogId is not null).ToList();
            var inserts = batch.Where(b => b.SourceLogId is null).ToList();

            foreach (var entry in updates)
            {
                await db.NotificationLogs
                    .Where(l => l.Id == entry.SourceLogId!.Value)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.Status, entry.Status)
                        .SetProperty(l => l.LatencyMs, entry.LatencyMs)
                        .SetProperty(l => l.Error, entry.Error)
                        .SetProperty(l => l.TimestampUtc, DateTime.UtcNow), token);
            }

            if (inserts.Count > 0)
            {
                db.NotificationLogs.AddRange(inserts.Select(e => new NotificationLog
                {
                    TimestampUtc = DateTime.UtcNow,
                    Channel = e.Channel,
                    EventType = e.EventType,
                    Title = Truncate(e.Title, 255),
                    Recipient = Truncate(e.Recipient, 255),
                    Status = e.Status,
                    LatencyMs = e.LatencyMs,
                    Error = e.Error,
                    PayloadPreview = e.PayloadPreview,
                    PayloadJson = e.PayloadJson
                }));
                await db.SaveChangesAsync(token);
            }
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];
    }

    /// <summary>
    /// 投递记录查询/清空/重试服务（Scoped）。
    /// 重试流程（R3 顺序）：行校验 → 载荷校验 → 渠道前置校验 → 置 Retrying → 入队（失败回置 Failed）。
    /// </summary>
    public class NotificationLogService : INotificationLogService
    {
        private readonly ScadaDbContext _db;
        private readonly IExternalNotificationQueue _queue;
        private readonly ILogger<NotificationLogService> _logger;

        public NotificationLogService(
            ScadaDbContext db,
            IExternalNotificationQueue queue,
            ILogger<NotificationLogService> logger)
        {
            _db = db;
            _queue = queue;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<NotificationLogDto>> GetRecentAsync(int limit = 500)
        {
            var rows = await _db.NotificationLogs
                .AsNoTracking()
                .OrderByDescending(l => l.Id)
                .Take(limit)
                .ToListAsync();
            return rows.Select(MapToDto).ToList();
        }

        /// <inheritdoc/>
        public async Task ClearAsync()
        {
            // 由控制器组合完成：清库 + 丢弃 Recorder 待写批次（避免清空后残留）。
            await _db.NotificationLogs.ExecuteDeleteAsync();
        }

        /// <inheritdoc/>
        public async Task<NotificationLogDto> RetryAsync(long id)
        {
            var log = await _db.NotificationLogs.FirstOrDefaultAsync(l => l.Id == id)
                ?? throw new BusinessException("投递记录不存在。");

            if (log.Status != "Failed")
            {
                throw new BusinessException("仅失败记录可重试。");
            }

            if (string.IsNullOrWhiteSpace(log.PayloadJson))
            {
                throw new BusinessException("投递载荷缺失或损坏，无法重试（测试发送记录不可重试）。");
            }

            ExternalMessage message;
            try
            {
                message = JsonSerializer.Deserialize<ExternalMessage>(log.PayloadJson)
                    ?? throw new JsonException("反序列化结果为空");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "投递记录 {Id} 载荷反序列化失败。", id);
                throw new BusinessException("投递载荷缺失或损坏，无法重试。");
            }

            // R3 渠道前置校验：渠道未启用时扇出无匹配状态会静默丢弃，先拦下给出明确提示
            if (!_queue.IsChannelEnabled(log.Channel))
            {
                throw new BusinessException("目标渠道当前未启用，请先在渠道配置中启用后重试。");
            }

            message.TargetChannel = log.Channel;
            message.SourceLogId = log.Id;

            log.Status = "Retrying";
            log.Error = null;
            await _db.SaveChangesAsync();

            // R3 入队校验：无启用渠道短路或主队列满载 → 回置 Failed 并抛业务异常
            if (!_queue.Enqueue(message))
            {
                log.Status = "Failed";
                log.Error = "队列满载，重试未受理，请稍后再试";
                await _db.SaveChangesAsync();
                throw new BusinessException(log.Error);
            }

            return MapToDto(log);
        }

        private static NotificationLogDto MapToDto(NotificationLog log) => new()
        {
            Id = log.Id,
            Timestamp = ToLocalDisplay(log.TimestampUtc),
            Channel = log.Channel,
            EventType = log.EventType,
            Title = log.Title,
            Recipient = log.Recipient,
            Status = log.Status,
            LatencyMs = log.LatencyMs,
            Error = log.Error,
            PayloadPreview = log.PayloadPreview
        };

        /// <summary>UTC → 本地时间展示（与 ExternalNotificationDecorator.ToLocalDisplay 同约定；
        /// ISO "T" 分隔格式保证前端 new Date() 跨浏览器解析）。</summary>
        private static string ToLocalDisplay(DateTime time)
        {
            var local = time.Kind switch
            {
                DateTimeKind.Local => time,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime(),
                _ => time.ToLocalTime()
            };
            return local.ToString("yyyy-MM-ddTHH:mm:ss");
        }
    }
}
