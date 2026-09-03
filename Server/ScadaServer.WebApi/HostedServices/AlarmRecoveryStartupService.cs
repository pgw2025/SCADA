using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Alarms;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.HostedServices
{
    /// <summary>
    /// 遗留未恢复报警启动巡检服务（修复 Bug#3）。
    /// <para>
    /// 报警触发/恢复状态机（DeviceWorker._ruleStates / _alarmStates）为进程内内存，进程重启即清零：
    /// 重启前已触发、值随后已恢复正常、但未来得及收到恢复事件的 AlarmRecord（RecoveredAt IS NULL）
    /// 会因状态机缺失而永远闭合不了。本服务在启动后巡检一次：
    /// <list type="number">
    /// <item>取全部未恢复报警记录，按（设备, 变量, 规则）分组；</item>
    /// <item>以 variablerealtime 表当前实时值为准判定该组是否已脱离报警条件
    ///   （规则报警用记录固化的 Condition/Threshold 经 <see cref="AlarmConditionEvaluator"/> 判定；
    ///   兜底报警用 DataPoint 模板 Min/Max 判定）；</item>
    /// <item>已脱离的组一次性补恢复（RecoveredAt/RecoveryValue = 当前实时值）并补发 SignalR 恢复事件；
    ///   仍处于报警态的组保持不动，等真实恢复事件。</item>
    /// </list>
    /// </para>
    /// </summary>
    public class AlarmRecoveryStartupService : BackgroundService
    {
        /// <summary>巡检延时：等待 RuntimeManager 加载设备并完成首轮快照落库（variablerealtime 有值），避免误判离线。</summary>
        private const int RecoveryCheckDelayMs = 5000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AlarmRecoveryStartupService> _logger;
        private readonly DatabaseInitializationStatus _dbReady;
        private readonly IScadaNotificationService _notification;

        public AlarmRecoveryStartupService(
            IServiceScopeFactory scopeFactory,
            ILogger<AlarmRecoveryStartupService> logger,
            DatabaseInitializationStatus dbReady,
            IScadaNotificationService notification)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dbReady = dbReady;
            _notification = notification;
        }

        /// <summary>巡检分组键：同键（设备+变量+规则）未恢复记录以当前实时值统一判定并整体闭合。</summary>
        private readonly record struct GroupKey(int DeviceId, string VariableKey, long? RuleId);

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 等待数据库初始化（迁移 + 种子）完成；模式与 RuntimeHostedService 一致。
            InitializationResult dbResult;
            try
            {
                dbResult = await _dbReady.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("遗留报警巡检在等待数据库初始化时被取消（应用正在关闭）。");
                return;
            }

            if (!dbResult.Succeeded)
            {
                if (dbResult.IsCancelled)
                {
                    _logger.LogWarning("数据库初始化被取消（应用正在关闭），遗留报警巡检跳过。");
                }
                else
                {
                    _logger.LogWarning("数据库初始化失败，遗留报警巡检跳过。");
                }
                return;
            }

            try
            {
                await Task.Delay(RecoveryCheckDelayMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var closed = await RecoverAsync(stoppingToken);
                _logger.LogInformation("遗留报警巡检完成：共补恢复 {ClosedCount} 条未恢复报警记录。", closed);
            }
            catch (OperationCanceledException)
            {
                // 应用关闭：正常退出路径
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "遗留报警巡检失败（本次未闭合，不影响后续真实恢复事件）。");
            }
        }

        private async Task<int> RecoverAsync(CancellationToken token)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

            var actives = await db.AlarmRecords
                .AsNoTracking()
                .Where(r => r.RecoveredAt == null)
                .ToListAsync(token);
            if (actives.Count == 0)
            {
                return 0;
            }

            var groups = actives
                .GroupBy(r => new GroupKey(r.DeviceId, r.VariableKey, r.RuleId))
                .ToList();
            var deviceIds = groups.Select(g => g.Key.DeviceId).Distinct().ToList();

            // 预取相关设备的最新实时值（DeviceId, VariableKey）→ Value
            var realtimes = await db.VariableRealtimes
                .AsNoTracking()
                .Where(v => deviceIds.Contains(v.DeviceId))
                .ToListAsync(token);
            var valueMap = realtimes.ToDictionary(v => (v.DeviceId, v.VariableKey), v => v.Value);

            // 预取兜底报警（RuleId IS NULL）判定所需的模板 Min/Max（经 AlarmRecord.DataPointId 关联）
            var dpIds = actives
                .Where(r => r.RuleId == null && r.DataPointId.HasValue)
                .Select(r => r.DataPointId!.Value)
                .Distinct()
                .ToList();
            var dpMap = dpIds.Count > 0
                ? await db.DataPoints.AsNoTracking().Where(d => dpIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, token)
                : new Dictionary<int, DataPoint>();

            var closedCount = 0;
            var now = DateTime.UtcNow;

            foreach (var g in groups)
            {
                // 无实时值（设备离线 / 变量从未成功采集）：不判定，保留 active 等真实恢复事件。
                if (!valueMap.TryGetValue((g.Key.DeviceId, g.Key.VariableKey), out var currentValue))
                {
                    continue;
                }

                var latest = g.OrderByDescending(r => r.TriggeredAt).First();
                var stillActive = g.Key.RuleId.HasValue
                    ? EvaluateRuleStillActive(latest, currentValue)
                    : EvaluateMinMaxStillActive(latest, currentValue, dpMap);

                if (stillActive)
                {
                    continue; // 值仍越限：保持未恢复，等待真实恢复事件闭合。
                }

                // 值已恢复正常 → 组级批量补恢复（同 MarkRecoveredAsync 语义）。
                var recoveryValue = currentValue.ToString(CultureInfo.InvariantCulture);
                if (g.Key.RuleId.HasValue)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE `AlarmRecords` SET `RecoveredAt` = {now}, `RecoveryValue` = {recoveryValue} WHERE `DeviceId` = {g.Key.DeviceId} AND `VariableKey` = {g.Key.VariableKey} AND `RuleId` = {g.Key.RuleId.Value} AND `RecoveredAt` IS NULL",
                        token);
                }
                else
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE `AlarmRecords` SET `RecoveredAt` = {now}, `RecoveryValue` = {recoveryValue} WHERE `DeviceId` = {g.Key.DeviceId} AND `VariableKey` = {g.Key.VariableKey} AND `RuleId` IS NULL AND `RecoveredAt` IS NULL",
                        token);
                }

                closedCount += g.Count();

                // 补发 SignalR 恢复事件（前端报警面板即时闭合；尽力而为，失败不影响落库）。
                await NotifyRecoveredAsync(latest, currentValue, now);
            }

            return closedCount;
        }

        /// <summary>规则报警：用记录固化 Condition/Threshold 判定当前值是否仍命中（命中=仍在报警态）。</summary>
        private static bool EvaluateRuleStillActive(AlarmRecord rec, double currentValue)
        {
            if (rec.Condition is not { } condition || rec.Threshold is not { } threshold)
            {
                return true; // 缺条件固化信息：保守视为仍在报警态。
            }
            return AlarmConditionEvaluator.IsMatched(condition, currentValue, threshold);
        }

        /// <summary>兜底报警（Min/Max）：按模板上下限判定当前值是否仍越界。</summary>
        private static bool EvaluateMinMaxStillActive(AlarmRecord rec, double currentValue, Dictionary<int, DataPoint> dpMap)
        {
            if (rec.DataPointId is not { } dpId || !dpMap.TryGetValue(dpId, out var dp))
            {
                return true; // 找不到模板：保守保留。
            }
            var high = dp.Max is { } max && currentValue > max;
            var low = dp.Min is { } min && currentValue < min;
            return high || low;
        }

        /// <summary>构造恢复事件并推送（fire-and-forget 通道，失败仅记录）。</summary>
        private async Task NotifyRecoveredAsync(AlarmRecord rec, double currentValue, DateTime now)
        {
            try
            {
                var evt = new AlarmEvent
                {
                    EventType = AlarmEventType.Recovered,
                    DeviceId = rec.DeviceId,
                    DeviceKey = rec.DeviceKey,
                    VariableKey = rec.VariableKey,
                    DataPointId = rec.DataPointId,
                    VariableName = rec.VariableName,
                    RuleId = rec.RuleId,
                    RuleName = rec.RuleName,
                    Level = rec.Level,
                    Condition = rec.Condition,
                    Threshold = rec.Threshold,
                    ActualValue = currentValue.ToString(CultureInfo.InvariantCulture),
                    Message = "服务重启后巡检：变量值已恢复正常",
                    Source = rec.Source,
                    TriggeredAt = now
                };
                await _notification.NotifyAlarmAsync(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "遗留报警补恢复通知推送失败: {DeviceKey}/{VariableKey}", rec.DeviceKey, rec.VariableKey);
            }
        }
    }
}
