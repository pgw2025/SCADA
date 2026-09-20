using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Runtime.Alarms
{
    /// <summary>
    /// 报警规则引擎实现。
    /// <para>
    /// 规则数据以不可变快照整体保存，读侧随时取本地引用遍历（引用替换原子，读安全）；
    /// 写侧（<see cref="ReloadAsync"/>）通过 lock 串行化并整体替换。内置定时器周期性下拉
    /// 最新活跃规则实现热重载，规则 CRUD 后无需重启服务即可生效（最坏延迟一个刷新周期）。
    /// 数据库未就绪时加载失败仅告警，下个周期自动重试，不影响引擎可用性。
    /// </para>
    /// </summary>
    public class AlarmRuleEngine : IAlarmRuleEngine
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AlarmRuleEngine> _logger;
        private readonly object _snapshotLock = new();

        /// <summary>
        /// 规则索引：(deviceId, variableKey) → 该变量名下的活跃规则（不可变，引用替换原子）。
        /// 一次 O(R) 分组建好，GetRules 改为 O(1) 命中，消除每变量每轮的 O(R) 全量扫描与 List 分配。
        /// </summary>
        private Dictionary<(int DeviceId, string VariableKey), IReadOnlyList<AlarmRuleSnapshot>> _index = new();

        /// <summary>去重后的规则数（诊断用）。</summary>
        private int _loadedRuleCount;

        private readonly Timer _refreshTimer;

        public AlarmRuleEngine(IServiceScopeFactory scopeFactory, ILogger<AlarmRuleEngine> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            // 启动定时热重载：DB 未就绪首轮可能失败，后续周期自动重试。
            _ = ReloadSilentAsync(); // 先尝试一次（进程内当前可能尚未迁移，失败静默）
            _refreshTimer = new Timer(_ => ReloadSilentAsync().GetAwaiter().GetResult(), null, RefreshInterval, RefreshInterval);
        }

        /// <inheritdoc/>
        public int LoadedRuleCount => Volatile.Read(ref _loadedRuleCount);

        /// <inheritdoc/>
        public IReadOnlyList<AlarmRuleSnapshot> GetRules(int deviceId, string variableKey)
        {
            var index = _index; // 读本地引用，后续访问同一份不可变快照
            return index.TryGetValue((deviceId, variableKey), out var list)
                ? list
                : Array.Empty<AlarmRuleSnapshot>();
        }

        /// <inheritdoc/>
        public Task ReloadAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
                var rules = db.AlarmRules
                    .AsNoTracking()
                    .Where(r => r.Active)
                    .Select(r => new AlarmRuleSnapshot
                    {
                        Id = r.Id,
                        Name = r.Name,
                        DeviceId = r.DeviceId,
                        VariableKey = r.VariableKey,
                        Condition = r.Condition,
                        Threshold = r.Threshold,
                        Level = r.Level,
                        Message = r.Message,
                        DebounceSeconds = r.DebounceSeconds
                    })
                    .ToList();

                // 构建索引：一次 O(R) 分组，替代每次 GetRules 的 O(R) 扫描 + ToList 分配。
                var index = rules
                    .GroupBy(r => (r.DeviceId, r.VariableKey))
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<AlarmRuleSnapshot>)g.ToList());

                lock (_snapshotLock)
                {
                    _index = index;
                    Volatile.Write(ref _loadedRuleCount, rules.Count);
                }

                // 仅当加载量为 0 时不必告警（可能确实无规则）；规则变化时记录便于运维。
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "报警规则加载失败（下个刷新周期自动重试）。");
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 静默加载（供 Timer 调用，确保内部异常不外泄到后台线程）。
        /// </summary>
        private async Task ReloadSilentAsync()
        {
            try
            {
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "报警规则静默加载失败（下个刷新周期自动重试）。");
            }
        }
    }
}