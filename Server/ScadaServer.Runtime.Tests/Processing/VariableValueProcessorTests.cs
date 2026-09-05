using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Alarms;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Alarms;
using ScadaServer.Runtime.Devices;
using ScadaServer.Runtime.Events;
using ScadaServer.Runtime.Processing;
using Xunit;

namespace ScadaServer.Runtime.Tests.Processing
{
    /// <summary>
    /// VariableValueProcessor 等价性单元测试（阶段三 Step 3.7）。
    /// <para>
    /// 覆盖用例与改造前 DeviceWorker 内联逻辑逐项对照（验收标准见 02-执行计划.md Step 3.7）：
    /// 1. 工程换算（模板表达式 / 实例覆盖 / 恒等 / 数字量透传）；
    /// 2. 回声抑制（LastBindingWriteValue 窗口内同值不广播、窗口外广播）；
    /// 3. 死区 / 存储模式（Change 变化存储含死区去抖、Cycle 周期存储）；
    /// 4. 质量降级（Good→Bad 跃迁通知一次、持续 Bad 不重复、僵尸值保留）；
    /// 5. 报警去重（Min/Max 兜底与规则报警的触发/恢复/去重、防抖、过期规则清理）；
    /// 6. 轮询入口 null → CommunicationError 路径；
    /// 7. 订阅入口（Good 全管线 + Bad 降级，Source=Subscription）。
    /// 附加：通知泵溢出丢弃与继续消费（Step 3.2 验收标准）。
    /// </para>
    /// </summary>
    public class VariableValueProcessorTests : IDisposable
    {
        private readonly Fixture _fixture = new();

        public void Dispose() => _fixture.Dispose();

        // ===================== 1. 工程换算 =====================

        [Fact]
        public async Task Polled_AppliesTemplateScaleExpression()
        {
            var def = CreateDefinition(key: "T1", scaleExpression: "x*0.1");
            var vr = CreateVariable(def);
            var runtime = CreateRuntime();

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 42.0, DateTime.UtcNow);

            Assert.Equal(4.2, Assert.IsType<double>(vr.Value), 5);
            Assert.Equal(VariableQuality.Good, vr.Quality);
        }

        [Fact]
        public async Task Polled_InstanceOverrideTakesPriorityOverTemplate()
        {
            var def = CreateDefinition(key: "T2"); // 模板无表达式
            var vr = CreateVariable(def, new DataPointMapping
            {
                Id = 2,
                DeviceId = 1,
                DataPointId = def.Id,
                IsEnabled = true,
                PollingIntervalMs = 1000,
                ScaleExpressionOverride = "(x-4000)/160"
            });
            var runtime = CreateRuntime();

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 4000.0, DateTime.UtcNow);
            Assert.Equal(0.0, Assert.IsType<double>(vr.Value), 5);

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 4160.0, DateTime.UtcNow.AddSeconds(1));
            Assert.Equal(1.0, Assert.IsType<double>(vr.Value), 5);
        }

        [Fact]
        public async Task Polled_EmptyExpressionIsIdentity_AndDigitalPassesThrough()
        {
            var runtime = CreateRuntime();

            var vr = CreateVariable(CreateDefinition(key: "T3"));
            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 7, DateTime.UtcNow);
            Assert.Equal(7, vr.Value); // 恒等：原类型原值

            var vrDigital = CreateVariable(CreateDefinition(key: "T4", scaleExpression: "x*0.1"));
            await _fixture.Processor.ApplyPolledAsync(runtime, vrDigital, true, DateTime.UtcNow);
            Assert.Equal(true, vrDigital.Value); // 数字量不参与换算
        }

        // ===================== 2. 回声抑制 =====================

        [Fact]
        public async Task Polled_EchoValueWithinWindow_SuppressesChangeEvent()
        {
            var def = CreateDefinition(key: "E1");
            var vr = CreateVariable(def);
            vr.Value = 41.0; // 此前值
            vr.LastBindingWriteValue = 42.0;
            vr.LastBindingWriteTime = DateTime.UtcNow.AddMilliseconds(-500); // 窗口 = max(1000,1000)*2 = 2000ms 内
            var runtime = CreateRuntime();

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 42.0, DateTime.UtcNow);

            Assert.False(vr.IsChanged); // 命中回声 → 不判为变化
            Assert.Empty(_fixture.ChangeBus.Events);
        }

        [Fact]
        public async Task Polled_EchoValueOutsideWindow_BroadcastsChange()
        {
            var def = CreateDefinition(key: "E2");
            var vr = CreateVariable(def);
            vr.Value = 41.0;
            vr.LastBindingWriteValue = 42.0;
            vr.LastBindingWriteTime = DateTime.UtcNow.AddSeconds(-10); // 窗口外
            var runtime = CreateRuntime();

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 42.0, DateTime.UtcNow);

            Assert.True(vr.IsChanged);
            var evt = Assert.Single(_fixture.ChangeBus.Events);
            Assert.Equal(VariableChangeSource.Polling, evt.Source);
            Assert.Equal(42.0, evt.Value);
        }

        // ===================== 3. 死区 / 存储模式 =====================

        [Fact]
        public async Task Polled_ChangeMode_HonorsDeadbandForEffectiveChange()
        {
            var def = CreateDefinition(key: "H1", storeMode: StoreModeEnum.Change, storeIntervalMs: 60000, deadBand: 1.0);
            var vr = CreateVariable(def);
            var runtime = CreateRuntime();
            var t0 = DateTime.UtcNow;

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 1.0, t0);
            Assert.Single(_fixture.History.Records); // 种子点

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 1.0, t0.AddMilliseconds(100));
            Assert.Single(_fixture.History.Records); // 值未变、未到期 → 不写

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 1.5, t0.AddMilliseconds(200));
            Assert.Single(_fixture.History.Records); // 变化但 |1.5-1.0|=0.5 ≤ 死区 1.0 → 非有效变化

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 3.0, t0.AddMilliseconds(300));
            Assert.Equal(2, _fixture.History.Records.Count); // |3.0-1.0|=2.0 > 死区 → 有效变化写入
        }

        [Fact]
        public async Task Polled_CycleMode_WritesOnlyWhenPeriodDue()
        {
            var def = CreateDefinition(key: "H2", storeMode: StoreModeEnum.Cycle, storeIntervalMs: 1000);
            var vr = CreateVariable(def);
            var runtime = CreateRuntime();
            var t0 = DateTime.UtcNow;

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 1.0, t0);
            Assert.Single(_fixture.History.Records); // 到期 → 种子点

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 2.0, t0.AddMilliseconds(500));
            Assert.Single(_fixture.History.Records); // 值变了但未到期 → 周期模式不写

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 2.0, t0.AddSeconds(1));
            Assert.Equal(2, _fixture.History.Records.Count); // 到期 → 写
        }

        // ===================== 4. 质量降级（僵尸值） =====================

        [Fact]
        public async Task Polled_NullValue_TransitionsToCommunicationErrorOnce_KeepsZombieValue()
        {
            var def = CreateDefinition(key: "Q1");
            var vr = CreateVariable(def);
            var runtime = CreateRuntime();
            var now = DateTime.UtcNow;

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 10.0, now);
            await WaitUntilAsync(() => _fixture.Notifications.VariableUpdates.Count == 1);

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, null, now.AddSeconds(1));
            Assert.Equal(VariableQuality.CommunicationError, vr.Quality);
            Assert.Equal(10.0, vr.Value); // 僵尸值：保留最近有效值
            await WaitUntilAsync(() => _fixture.Notifications.VariableUpdates.Count == 2);

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, null, now.AddSeconds(2));
            Assert.Equal(2, _fixture.Notifications.VariableUpdates.Count); // 持续失败不重复通知

            var degraded = _fixture.Notifications.VariableUpdates.Last();
            Assert.Equal(VariableQuality.CommunicationError, degraded.Quality);
            Assert.Equal(10.0, degraded.Value); // 降级通知携带最近有效值
        }

        [Fact]
        public async Task Subscribed_BadQuality_DropsValue_AndDegradesOnce()
        {
            var def = CreateDefinition(key: "Q2");
            var vr = CreateVariable(def);
            var runtime = CreateRuntime();
            var now = DateTime.UtcNow;

            await _fixture.Processor.ApplySubscribedAsync(runtime, vr, 1.0, VariableQuality.Good, now);
            await WaitUntilAsync(() => _fixture.Notifications.VariableUpdates.Count == 1);

            await _fixture.Processor.ApplySubscribedAsync(runtime, vr, 2.0, VariableQuality.Bad, now.AddSeconds(1));
            Assert.Equal(VariableQuality.CommunicationError, vr.Quality);
            Assert.Equal(1.0, vr.Value); // Bad 值被丢弃
            await WaitUntilAsync(() => _fixture.Notifications.VariableUpdates.Count == 2);

            await _fixture.Processor.ApplySubscribedAsync(runtime, vr, 3.0, VariableQuality.Uncertain, now.AddSeconds(2));
            Assert.Equal(2, _fixture.Notifications.VariableUpdates.Count); // Uncertain 持续降级不重复
        }

        // ===================== 5. 报警去重 =====================

        [Fact]
        public async Task Polled_MinMaxLimit_TriggersOnce_RecoversOnce()
        {
            var def = CreateDefinition(key: "A1", min: 0, max: 100);
            var vr = CreateVariable(def);
            var runtime = CreateRuntime();
            var now = DateTime.UtcNow;

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 150.0, now);
            Assert.Single(_fixture.AlarmRecorder.Events); // 触发
            Assert.Equal(AlarmEventType.Triggered, _fixture.AlarmRecorder.Events.First().EventType);

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 160.0, now.AddSeconds(1));
            Assert.Single(_fixture.AlarmRecorder.Events); // 持续越限 → 去重不重复推送

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 50.0, now.AddSeconds(2));
            Assert.Equal(2, _fixture.AlarmRecorder.Events.Count); // 恢复
            Assert.Equal(AlarmEventType.Recovered, _fixture.AlarmRecorder.Events.Last().EventType);
        }

        [Fact]
        public async Task Polled_RuleAlarm_TriggersOnce_RecoversOnce()
        {
            _fixture.RuleEngine.Rules =
            [
                new AlarmRuleSnapshot
                {
                    Id = 1,
                    DeviceId = 1,
                    VariableKey = "R1",
                    Name = "高限报警",
                    Condition = TriggerConditionEnum.GreaterThan,
                    Threshold = 100,
                    Level = AlarmLevelEnum.High
                }
            ];

            var vr = CreateVariable(CreateDefinition(key: "R1"));
            var runtime = CreateRuntime();
            var now = DateTime.UtcNow;

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 150.0, now);
            Assert.Single(_fixture.AlarmRecorder.Events);

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 160.0, now.AddSeconds(1));
            Assert.Single(_fixture.AlarmRecorder.Events); // 持续命中 → 去重

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 50.0, now.AddSeconds(2));
            Assert.Equal(2, _fixture.AlarmRecorder.Events.Count); // 恢复
        }

        [Fact]
        public async Task Polled_RuleDebounce_RequiresSustainedHit_ToTrigger()
        {
            _fixture.RuleEngine.Rules =
            [
                new AlarmRuleSnapshot
                {
                    Id = 2,
                    DeviceId = 1,
                    VariableKey = "R2",
                    Name = "防抖报警",
                    Condition = TriggerConditionEnum.GreaterThan,
                    Threshold = 100,
                    Level = AlarmLevelEnum.Medium,
                    DebounceSeconds = 1
                }
            ];

            var vr = CreateVariable(CreateDefinition(key: "R2"));
            var runtime = CreateRuntime();
            var now = DateTime.UtcNow;

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 150.0, now);
            Assert.Empty(_fixture.AlarmRecorder.Events); // 首次命中仅记录观察起点

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 160.0, now.AddSeconds(1));
            Assert.Empty(_fixture.AlarmRecorder.Events); // 仍在防抖窗口内（真实间隔仅为毫秒级）

            await Task.Delay(1500); // 越过 1s 防抖窗口
            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 170.0, DateTime.UtcNow);
            Assert.Single(_fixture.AlarmRecorder.Events); // 持续命中 → 正式触发

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 50.0, DateTime.UtcNow);
            Assert.Equal(2, _fixture.AlarmRecorder.Events.Count); // 恢复
        }

        [Fact]
        public async Task Polled_StaleRuleState_IsPruned_AndRuleCanTriggerAgain()
        {
            var rule = new AlarmRuleSnapshot
            {
                Id = 3,
                DeviceId = 1,
                VariableKey = "R3",
                Name = "高限报警",
                Condition = TriggerConditionEnum.GreaterThan,
                Threshold = 100,
                Level = AlarmLevelEnum.High
            };
            _fixture.RuleEngine.Rules = [rule];

            var vr = CreateVariable(CreateDefinition(key: "R3"));
            var runtime = CreateRuntime();
            var now = DateTime.UtcNow;

            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 150.0, now);
            Assert.Single(_fixture.AlarmRecorder.Events); // 触发

            _fixture.RuleEngine.Rules = []; // 规则被热删除
            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 120.0, now.AddSeconds(1));
            Assert.Single(_fixture.AlarmRecorder.Events); // 删除后无兜底 → 无新事件；过期状态被清理

            _fixture.RuleEngine.Rules = [rule]; // 规则重新加回
            await _fixture.Processor.ApplyPolledAsync(runtime, vr, 150.0, now.AddSeconds(2));
            Assert.Equal(2, _fixture.AlarmRecorder.Events.Count); // 旧状态已清理 → 重新触发而非去重
        }

        // ===================== 6. 订阅入口 =====================

        [Fact]
        public async Task Subscribed_GoodValue_RunsFullPipeline_WithSubscriptionSource()
        {
            var def = CreateDefinition(key: "S1", scaleExpression: "x*0.5", storeMode: StoreModeEnum.Change, storeIntervalMs: 60000);
            var vr = CreateVariable(def);
            var runtime = CreateRuntime();
            var now = DateTime.UtcNow;

            await _fixture.Processor.ApplySubscribedAsync(runtime, vr, 10.0, VariableQuality.Good, now);

            // 订阅回调的值是工程值（驱动已完成换算），这里直接使用
            Assert.Equal(10.0, vr.Value);
            var evt = Assert.Single(_fixture.ChangeBus.Events);
            Assert.Equal(VariableChangeSource.Subscription, evt.Source);
            Assert.Single(_fixture.History.Records); // 种子点
        }

        // ===================== 7. 通知泵溢出（Step 3.2 验收） =====================

        [Fact]
        public async Task NotificationPump_Overflow_DropsOldest_AndKeepsConsuming()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocking = new FakeBlockingNotificationService(gate);
            var processor = new VariableValueProcessor(
                blocking, _fixture.History, _fixture.ChangeBus, _fixture.RuleEngine,
                _fixture.AlarmRecorder, _fixture.Realtime, NullLogger<VariableValueProcessor>.Instance);

            var runtime = CreateRuntime(deviceId: 99, key: "OVF");
            var vr = CreateVariable(CreateDefinition(key: "O1"));
            var t0 = DateTime.UtcNow;

            try
            {
                // 第一条启动消费任务并使其阻塞在门闩上（占用通道 1 个槽位的处理中项）
                await processor.ApplyPolledAsync(runtime, vr, 1.0, t0);
                await blocking.Started.Task;

                // 再入队 2999 条（交替值保证每条都是"变化"→ 触发通知入队）：
                // 通道容量 2048，缓冲区满后按 DropOldest 丢弃最旧
                for (var i = 1; i < 3000; i++)
                {
                    await processor.ApplyPolledAsync(runtime, vr, i % 2 == 0 ? 2.0 : 3.0, t0.AddMilliseconds(i));
                }

                // 放行消费：最终应恰好处理 1（占用项）+ 2048（缓冲）条，其余 ~951 条被丢弃
                gate.TrySetResult(true);
                await WaitUntilAsync(() => Volatile.Read(ref blocking.Recorded) == 2049);
            }
            finally
            {
                processor.StopDevice(99);
            }
        }

        // ===================== 测试工具 =====================

        private static DeviceRuntime CreateRuntime(int deviceId = 1, string key = "DEV1")
            => new(new Device { Id = deviceId, Key = key });

        private static DataPoint CreateDefinition(
            string key,
            string? scaleExpression = null,
            double? deadBand = null,
            double? min = null,
            double? max = null,
            StoreModeEnum storeMode = StoreModeEnum.None,
            int storeIntervalMs = 300000)
            => new()
            {
                Id = 1,
                Key = key,
                Name = key,
                DataType = DataTypeEnum.REAL,
                ScaleExpression = scaleExpression,
                DeadBand = deadBand,
                Min = min,
                Max = max,
                StoreMode = storeMode,
                StoreIntervalMs = storeIntervalMs
            };

        private static VariableRuntime CreateVariable(DataPoint def, DataPointMapping? instance = null)
            => new()
            {
                Definition = def,
                Instance = instance ?? new DataPointMapping
                {
                    Id = 1,
                    DeviceId = 1,
                    DataPointId = def.Id,
                    IsEnabled = true,
                    PollingIntervalMs = 1000
                }
            };

        /// <summary>轮询等待异步通知泵消费（默认 3s 超时，超时抛异常标记行为回归）。</summary>
        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    throw new TimeoutException("异步通知泵消费未在超时时间内达到预期状态。");
                }
                await Task.Delay(20);
            }
        }

        /// <summary>每测试夹具：独立桩服务 + 处理器实例（xunit 每测试新建，互不污染）。</summary>
        private sealed class Fixture : IDisposable
        {
            public FakeNotificationService Notifications { get; } = new();
            public FakeHistoryRecorder History { get; } = new();
            public FakeChangeBus ChangeBus { get; } = new();
            public FakeAlarmRuleEngine RuleEngine { get; } = new();
            public FakeAlarmRecorder AlarmRecorder { get; } = new();
            public FakeRealtimeSnapshot Realtime { get; } = new();
            public VariableValueProcessor Processor { get; }

            public Fixture()
            {
                Processor = new VariableValueProcessor(
                    Notifications, History, ChangeBus, RuleEngine, AlarmRecorder, Realtime,
                    NullLogger<VariableValueProcessor>.Instance);
            }

            public void Dispose() => Processor.StopDevice(1);
        }

        // ===================== 桩服务 =====================

        private sealed class FakeNotificationService : IScadaNotificationService
        {
            public ConcurrentQueue<(int DeviceId, string VariableKey, object? Value, VariableQuality Quality, DateTime UpdateTime)> VariableUpdates { get; } = new();
            public ConcurrentQueue<AlarmEvent> AlarmEvents { get; } = new();

            public Task NotifyVariableUpdateAsync(int deviceId, string variableKey, object? value, VariableQuality quality, DateTime updateTime)
            {
                VariableUpdates.Enqueue((deviceId, variableKey, value, quality, updateTime));
                return Task.CompletedTask;
            }

            public Task NotifyDeviceStatusAsync(int deviceId, DeviceStatus status) => Task.CompletedTask;
            public Task NotifySystemAlarmAsync(int deviceId, string variableKey, string variableName, string message, string level) => Task.CompletedTask;
            public Task NotifyAlarmAsync(AlarmEvent evt) { AlarmEvents.Enqueue(evt); return Task.CompletedTask; }
            public Task NotifyScriptExecutionAsync(ScriptExecutionEvent evt) => Task.CompletedTask;
        }

        /// <summary>阻塞式通知桩：首条消费触发 Started，随后阻塞在门闩上，用于制造通道溢出。记录最终处理条数。</summary>
        private sealed class FakeBlockingNotificationService : IScadaNotificationService
        {
            private readonly TaskCompletionSource<bool> _gate;
            public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public long Recorded;

            public FakeBlockingNotificationService(TaskCompletionSource<bool> gate) => _gate = gate;

            public async Task NotifyVariableUpdateAsync(int deviceId, string variableKey, object? value, VariableQuality quality, DateTime updateTime)
            {
                Started.TrySetResult(true);
                await _gate.Task;
                Interlocked.Increment(ref Recorded);
            }

            public Task NotifyDeviceStatusAsync(int deviceId, DeviceStatus status) => Task.CompletedTask;
            public Task NotifySystemAlarmAsync(int deviceId, string variableKey, string variableName, string message, string level) => Task.CompletedTask;
            public Task NotifyAlarmAsync(AlarmEvent evt) => Task.CompletedTask;
            public Task NotifyScriptExecutionAsync(ScriptExecutionEvent evt) => Task.CompletedTask;
        }

        private sealed class FakeHistoryRecorder : IHistoryRecorder
        {
            public ConcurrentQueue<HistoryRecord> Records { get; } = new();
            public record HistoryRecord(int DeviceId, string DeviceKey, string VariableKey, string VariableName, double Value, string? RawValue, string? Quality, DateTime SampleTime);

            public void Record(int deviceId, string deviceKey, string variableKey, string variableName, double value, string? rawValue, string? quality, DateTime sampleTime)
                => Records.Enqueue(new HistoryRecord(deviceId, deviceKey, variableKey, variableName, value, rawValue, quality, sampleTime));

            public void Complete() { }
        }

        private sealed class FakeChangeBus : IVariableChangeBus
        {
            public List<VariableChangeEvent> Events { get; } = [];

            public FakeChangeBus() => VariableChanged += (_, e) => Events.Add(e);

            public event EventHandler<VariableChangeEvent>? VariableChanged;

            public void Publish(VariableChangeEvent evt) => VariableChanged?.Invoke(this, evt);
        }

        private sealed class FakeAlarmRuleEngine : IAlarmRuleEngine
        {
            public List<AlarmRuleSnapshot> Rules { get; set; } = [];

            public Task ReloadAsync() => Task.CompletedTask;
            public int LoadedRuleCount => Rules.Count;
            public IReadOnlyList<AlarmRuleSnapshot> GetRules(int deviceId, string variableKey)
                => Rules.Where(r => r.DeviceId == deviceId && r.VariableKey == variableKey).ToList();
        }

        private sealed class FakeAlarmRecorder : IAlarmRecorder
        {
            public ConcurrentQueue<AlarmEvent> Events { get; } = new();
            public void Record(AlarmEvent evt) => Events.Enqueue(evt);
            public void Complete() { }
        }

        private sealed class FakeRealtimeSnapshot : IRealtimeSnapshotService
        {
            public ConcurrentQueue<object> Updates { get; } = new();

            public void Update(int deviceId, string deviceKey, string variableKey, string variableName, double value, string? rawValue, string? quality, DateTime timestamp)
                => Updates.Enqueue(new { deviceId, deviceKey, variableKey, value, rawValue, quality, timestamp });
        }
    }
}
