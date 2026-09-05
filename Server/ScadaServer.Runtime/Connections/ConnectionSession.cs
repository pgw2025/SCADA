using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Runtime.Devices;
using ScadaServer.Runtime.Processing;

namespace ScadaServer.Runtime.Connections
{
    /// <summary>
    /// 连接会话：一个 <see cref="DeviceConnection"/> 对应一条物理连接（进程内单例），
    /// 其下挂载的多台设备共享同一驱动实例。
    /// <para>
    /// 职责：持有唯一驱动实例；设备挂载/卸载（引用计数生命周期，末位离场销毁会话）；
    /// 连接状态扇出到挂载设备（复用设备级 ConnectionStateChanged → RuntimeManager 推送链路）；
    /// 会话级重连归口（闸门 + 探测 + 重建，P3）。
    /// </para>
    /// <para>
    /// 不职责：轮询调度（DeviceScheduler / DeviceWorker）、报警判定、历史落库、变量读写。
    /// 设备运行时不随会话重建而重建（变量值/报警计数/RunState 全部存活）。
    /// </para>
    /// </summary>
    public sealed class ConnectionSession : IRuntimeConnection, IAsyncDisposable
    {
        private DeviceConnection _connection;   // 配置实体（ConfigJson 真相源）；热更新时经 UpdateConnectionConfig 整体替换
        private readonly string _protocolKey;
        private readonly IProtocolDriverFactory _driverFactory;
        private readonly IVariableValueProcessor _processor;
        private readonly ILogger _logger;

        /// <summary>驱动实例：仅在会话生命周期方法内（Connect/Reconnect/Dispose）替换，读写路径经 volatile 快照。</summary>
        private volatile IProtocolDriver? _driver;

        /// <summary>挂载设备表：key = deviceId。挂载/卸载与会话销毁由 _lifecycleLock 串行化。</summary>
        private readonly ConcurrentDictionary<int, DeviceRuntime> _mounted = new();

        /// <summary>
        /// 订阅路由表：key = instanceId（DataPointMapping.Id，跨设备全局唯一），value = 归属设备 + 变量运行时快照。
        /// <para>
        /// 快照中的 Vr 引用仅用于构造驱动订阅调用与退订匹配；回调路由（<see cref="OnSubscriptionValue"/>）
        /// 先查路由表命中后，再经 _mounted + runtime.Variables 实时解析目标运行时——不依赖快照引用（决策 D1，
        /// 避免重载窗口内快照引用指向已被替换的运行时）。
        /// </para>
        /// <para>与 SyncSubscriptionsAsync 差量同步维护；会话重建时整体清空（新驱动订阅为空，必须全量重订）。</para>
        /// </summary>
        private readonly ConcurrentDictionary<int, (int DeviceId, VariableRuntime Vr)> _subscriptionRoutes = new();

        /// <summary>订阅同步去抖调度状态：_syncRunning=同步循环在跑，_syncDirty=执行期间又有新请求（Interlocked）。</summary>
        private int _syncRunning;
        private int _syncDirty;

        /// <summary>订阅同步去抖合并窗口（毫秒）：挂载/卸载/热更集中发生时合并为一次同步，避免抖动（P2-8）。</summary>
        private const int SyncMergeWindowMs = 100;

        /// <summary>订阅失败重试退避：初始 30s，失败翻倍，上限 5min（决策 D4，不回退轮询）。</summary>
        private const int InitialSubscriptionRetryMs = 30_000;
        private const int MaxSubscriptionRetryMs = 300_000;

        /// <summary>下次允许执行订阅同步的时刻（UTC）：退避窗口内仅退订、不做新增订阅。仅在同步循环内读写。</summary>
        private DateTime _subscriptionBackoffUntil = DateTime.MinValue;

        /// <summary>当前退避步长（毫秒）：成功后复位为初始值，失败翻倍至上限。仅在同步循环内读写。</summary>
        private int _subscriptionBackoffMs = InitialSubscriptionRetryMs;

        /// <summary>生命周期锁：串行化挂载/卸载/建连/销毁/重建。按项目约定永不 Dispose。</summary>
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

        private volatile bool _disposed;
        private volatile DeviceConnectionState _state = DeviceConnectionState.Initializing;

        // 重连闸门：0=空闲，1=重建在途（Interlocked，多设备并发断线信号收敛为一次重建）。
        private int _reconnecting;

        // 断线信号聚合计数（Interlocked）：用于判定"会话是否持续故障"的辅助观测。
        private int _failureSignals;

        /// <summary>当前驱动实例（无会话/未连接时为 null）。</summary>
        public IProtocolDriver? Driver => _driver;

        /// <summary>会话是否已销毁（终态，仅 DisposeAsync 置位）。</summary>
        public bool IsDisposed => _disposed;

        /// <summary>当前挂载设备数。</summary>
        public int MountCount => _mounted.Count;

        /// <summary>是否无任何挂载设备。</summary>
        public bool IsEmpty => _mounted.IsEmpty;

        /// <summary>挂载设备快照（ConcurrentDictionary 弱一致遍历的稳定副本，供运行期枚举/遍历卸载）。</summary>
        public IReadOnlyCollection<DeviceRuntime> MountedSnapshot => _mounted.Values.ToArray();

        /// <summary>刷新连接配置实体引用（配置热更新用）。仅更新 ConfigJson/IsEnabled 等参数真相源，不改变协议/挂载关系。</summary>
        public void UpdateConnectionConfig(DeviceConnection connection) => _connection = connection;

        /// <summary>最近一次状态翻转时刻（UTC）。</summary>
        public DateTime StateChangedAt { get; private set; } = DateTime.UtcNow;

        public int ConnectionId => _connection.Id;

        public string Key => string.IsNullOrEmpty(_connection.Name) ? $"#{_connection.Id}" : _connection.Name;

        public string ConfigJson => _connection.ConfigJson ?? "{}";

        /// <summary>
        /// 会话连接状态。setter 扇出到全部挂载设备（每台设备触发其 ConnectionStateChanged，
        /// 进而由 RuntimeManager 推送状态并落库）。仅在值变化时推进 StateChangedAt。
        /// </summary>
        public DeviceConnectionState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                StateChangedAt = DateTime.UtcNow;
                foreach (var rt in _mounted.Values)
                    rt.ConnectionState = value;
            }
        }

        /// <summary>初始化会话。</summary>
        /// <param name="connection">连接配置实体（ConfigJson 真相源）。</param>
        /// <param name="protocolKey">协议驱动键（Protocol.Key，用于工厂创建驱动）。</param>
        /// <param name="driverFactory">驱动工厂。</param>
        /// <param name="valueProcessor">变量值处理管线（订阅回调经此统一入管线，与轮询路径共用下游处理）。</param>
        /// <param name="logger">日志组件。</param>
        public ConnectionSession(
            DeviceConnection connection,
            string protocolKey,
            IProtocolDriverFactory driverFactory,
            IVariableValueProcessor valueProcessor,
            ILogger logger)
        {
            _connection = connection;
            _protocolKey = protocolKey;
            _driverFactory = driverFactory;
            _processor = valueProcessor ?? throw new ArgumentNullException(nameof(valueProcessor));
            _logger = logger;
        }

        /// <summary>
        /// 建连（幂等）：已有驱动实例直接返回已连接。首次建连或上次失败后的重试由调用方结合 State 决定触发。
        /// </summary>
        /// <returns>是否已连接。</returns>
        public async Task<bool> ConnectAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return false;
                if (_driver != null) return true;

                var driver = _driverFactory.CreateDriver(_protocolKey);
                bool ok;
                try
                {
                    ok = await driver.ConnectAsync(this);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "连接 {ConnKey}(#{ConnId}) 建连异常。", Key, ConnectionId);
                    await SafeDisposeDriverAsync(driver);
                    State = DeviceConnectionState.Error;
                    return false;
                }

                if (ok)
                {
                    _driver = driver;
                    State = DeviceConnectionState.Connected;
                    return true;
                }

                await SafeDisposeDriverAsync(driver);
                State = DeviceConnectionState.Error;
                return false;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>挂载一台设备到本会话（引用计数），保持当前会话状态与设备同步。</summary>
        public async Task MountAsync(DeviceRuntime runtime)
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return;
                _mounted[runtime.Device.Id] = runtime;
                runtime.Session = this;
                runtime.ConnectionState = State;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>从会话卸载一台设备并解绑 Session。返回会话是否已无任何挂载设备（由调用方据此销毁会话）。</summary>
        public async Task<bool> UnmountAsync(int deviceId)
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_mounted.TryRemove(deviceId, out var rt))
                {
                    rt.Session = null;
                }
                return _mounted.IsEmpty;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        // ===================== 订阅协调器（阶段四，Step 4.1） =====================

        /// <summary>
        /// 触发一次去抖的订阅差量同步（fire-and-forget）。
        /// <para>
        /// 调用方（挂载/卸载/重连/配置热更）只负责"声明变更"；本方法去抖合并（100ms 窗口）后执行
        /// <see cref="SyncSubscriptionsAsync"/>。调用方<b>绝不在会话锁内 await 同步结果</b>——
        /// 同步内部会做驱动 IO（订阅/退订），必须在锁外执行（P2-7 纪律）。
        /// </para>
        /// </summary>
        public void ScheduleSync()
        {
            if (_disposed) return;
            Interlocked.Exchange(ref _syncDirty, 1);
            if (Interlocked.CompareExchange(ref _syncRunning, 1, 0) != 0)
            {
                return; // 同步循环已在跑：新请求已置 dirty，循环结束后会再跑一轮收敛。
            }
            _ = Task.Run(RunSyncLoopAsync);
        }

        /// <summary>
        /// 订阅同步循环：dirty 置位则执行一轮（去抖 → 差量同步），执行期间新请求置 dirty → 循环继续；
        /// 循环退出瞬间的竞争由 finally 重新入队兜底。
        /// </summary>
        private async Task RunSyncLoopAsync()
        {
            try
            {
                while (Volatile.Read(ref _syncDirty) != 0)
                {
                    Interlocked.Exchange(ref _syncDirty, 0);
                    await Task.Delay(SyncMergeWindowMs);
                    if (_disposed) return;
                    await SyncSubscriptionsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接 {ConnKey}(#{ConnId}) 订阅同步循环异常（已忽略，后续变更会重新触发）。", Key, ConnectionId);
            }
            finally
            {
                Interlocked.Exchange(ref _syncRunning, 0);
                // 终态竞争：循环刚检查完 dirty 又收到新请求（dirty=1 但循环已退出）→ 重新入队。
                if (Volatile.Read(ref _syncDirty) != 0
                    && Interlocked.CompareExchange(ref _syncRunning, 1, 0) == 0)
                {
                    _ = Task.Run(RunSyncLoopAsync);
                }
            }
        }

        /// <summary>
        /// 订阅差量同步（全程<b>不持会话 _lifecycleLock</b>，驱动 IO 在锁外，P2-7 纪律）：
        /// 期望集 = 全部挂载设备的「启用 + Subscription 模式」变量；差量计算 toAdd / toRemove 后
        /// 先退订再订阅；成功更新路由表；快照竞争（同步期间挂载/卸载变化）由下一次去抖同步收敛。
        /// </summary>
        private async Task SyncSubscriptionsAsync()
        {
            var driver = _driver;
            if (driver == null) return;

            // 期望集快照（弱一致遍历，够用——差量由下一次同步收敛）。
            var expected = new Dictionary<int, (int DeviceId, VariableRuntime Vr)>();
            foreach (var rt in _mounted.Values)
            {
                foreach (var vr in rt.Variables.Values)
                {
                    if (vr.IsEnabled && vr.UpdateMode == UpdateModeEnum.Subscription)
                    {
                        expected[vr.InstanceId] = (rt.Device.Id, vr);
                    }
                }
            }

            // toRemove = 路由 - 期望：设备卸载/变量删除/切回轮询后退订。
            var toRemove = new List<VariableRuntime>();
            foreach (var pair in _subscriptionRoutes)
            {
                if (!expected.ContainsKey(pair.Key))
                {
                    toRemove.Add(pair.Value.Vr);
                }
            }
            if (toRemove.Count > 0)
            {
                try
                {
                    await driver.UnsubscribeAsync(toRemove);
                }
                catch (Exception ex)
                {
                    // 退订失败不影响本轮其它处理：路由表照常移除（本地语义收敛），下次同步不会重试退订。
                    _logger.LogWarning(ex, "连接 {ConnKey}(#{ConnId}) 退订 {Count} 个变量失败（本地路由已移除）。",
                        Key, ConnectionId, toRemove.Count);
                }
                foreach (var vr in toRemove)
                {
                    _subscriptionRoutes.TryRemove(vr.InstanceId, out _);
                }
            }

            // 退避窗口检查：退订始终执行；新增订阅在退避窗口内跳过（避免失败风暴），由退避调度重试。
            if (DateTime.UtcNow < _subscriptionBackoffUntil)
            {
                _logger.LogDebug("连接 {ConnKey}(#{ConnId}) 订阅新增处于退避窗口内，跳过本轮（{Ms}ms 后重试）。",
                    Key, ConnectionId, (int)(_subscriptionBackoffUntil - DateTime.UtcNow).TotalMilliseconds);
                return;
            }

            // toAdd = 期望 - 路由：新增/恢复订阅。
            var toAdd = new List<VariableRuntime>();
            foreach (var pair in expected)
            {
                if (!_subscriptionRoutes.ContainsKey(pair.Key))
                {
                    toAdd.Add(pair.Value.Vr);
                }
            }
            if (toAdd.Count == 0)
            {
                // 无新增：退避步长复位（订阅全部稳定的信号）。
                _subscriptionBackoffMs = InitialSubscriptionRetryMs;
                return;
            }

            try
            {
                await driver.SubscribeAsync(toAdd, OnSubscriptionValue);
                foreach (var vr in toAdd)
                {
                    _subscriptionRoutes[vr.InstanceId] = expected[vr.InstanceId];
                }
                _subscriptionBackoffMs = InitialSubscriptionRetryMs;
                _logger.LogInformation("连接 {ConnKey}(#{ConnId}) 订阅 {Count} 个变量成功。", Key, ConnectionId, toAdd.Count);
            }
            catch (Exception ex)
            {
                // 订阅失败（决策 D4）：toAdd 变量置 CommunicationError（锁内写值，与采集锁串行化），
                // 记 Warning + 退避调度下一次同步。不回退轮询——订阅失败不代表连接死亡。
                _logger.LogWarning(ex, "连接 {ConnKey}(#{ConnId}) 订阅 {Count} 个变量失败，进入退避重试。",
                    Key, ConnectionId, toAdd.Count);
                foreach (var vr in toAdd)
                {
                    if (!_mounted.TryGetValue(expected[vr.InstanceId].DeviceId, out var rt))
                    {
                        continue;
                    }
                    try
                    {
                        await rt.Lock.WaitAsync();
                        try
                        {
                            vr.Quality = VariableQuality.CommunicationError;
                        }
                        finally
                        {
                            rt.Lock.Release();
                        }
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogDebug(innerEx, "连接 {ConnKey}(#{ConnId}) 订阅失败置位变量质量异常（已忽略）。",
                            Key, ConnectionId);
                    }
                }
                ScheduleBackoffRetry();
            }
        }

        /// <summary>
        /// 订阅回调（驱动协议栈线程，P2-9 纪律）：全程 try-catch，禁止阻塞、禁止抛异常。
        /// 路由先查表命中，再实时解析目标运行时（决策 D1）；打点为内存写、处理为 fire-and-forget 入口。
        /// </summary>
        private void OnSubscriptionValue(int instanceId, object? value, VariableQuality quality)
        {
            try
            {
                if (_disposed) return;
                if (!_subscriptionRoutes.TryGetValue(instanceId, out var route)) return; // 已退订
                if (!_mounted.TryGetValue(route.DeviceId, out var runtime)) return;       // 已卸载
                if (!runtime.Variables.TryGetValue(instanceId, out var vr)) return;       // 重载窗口/变量已删

                // 通讯打点（P1-2）：订阅回调即通讯成功的硬证据——清零连续失败、刷新通讯时间；
                // Error 态自愈为 Connected（连接共享架构下真实连接死亡时订阅同样静默，由看门狗兜底）。
                var now = DateTime.UtcNow;
                runtime.LastCommunicationTime = now;
                runtime.ConsecutiveFailureCount = 0;
                if (runtime.ConnectionState != DeviceConnectionState.Connected)
                {
                    runtime.ConnectionState = DeviceConnectionState.Connected;
                }

                // 处理（fire-and-forget）：处理器内部全链路异常兜底，不阻塞协议栈线程。
                _ = SafeApplySubscribedAsync(runtime, vr, value, quality, now);
            }
            catch (Exception ex)
            {
                // 回调不得抛出：任何意外仅记 Error（含 instanceId 上下文）后吞掉。
                _logger.LogError(ex, "连接 {ConnKey}(#{ConnId}) 订阅回调处理异常（instanceId={InstanceId}）。",
                    Key, ConnectionId, instanceId);
            }
        }

        /// <summary>订阅值处理入口（fire-and-forget 包裹）：处理器未兜住的意外异常就地记录，杜绝未观察异常。</summary>
        private async Task SafeApplySubscribedAsync(
            DeviceRuntime runtime, VariableRuntime vr, object? value, VariableQuality quality, DateTime now)
        {
            try
            {
                await _processor.ApplySubscribedAsync(runtime, vr, value, quality, now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订阅值处理失败（instanceId={InstanceId}，变量 {VariableKey}）。",
                    vr.InstanceId, vr.Key);
            }
        }

        /// <summary>订阅失败退避调度：步长翻倍（上限 5min），到点后重新触发一次差量同步。</summary>
        private void ScheduleBackoffRetry()
        {
            var delayMs = _subscriptionBackoffMs;
            _subscriptionBackoffMs = Math.Min(_subscriptionBackoffMs * 2, MaxSubscriptionRetryMs);
            _subscriptionBackoffUntil = DateTime.UtcNow.AddMilliseconds(delayMs);
            _logger.LogWarning("连接 {ConnKey}(#{ConnId}) 订阅失败退避 {Ms}ms 后重试。", Key, ConnectionId, delayMs);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs);
                    ScheduleSync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "连接 {ConnKey}(#{ConnId}) 订阅退避重试调度异常（已忽略）。", Key, ConnectionId);
                }
            });
        }

        // ===================== P3 会话级重连归口 =====================

        /// <summary>
        /// 断线信号上抛入口（DeviceWorker 断线判定触发）。聚合 + 闸门去重：
        /// 先探测连接存活——存活则判定设备级故障，不重建会话（避免坏地址设备反复触发健康连接重建）；
        /// 探测死亡则进入重建路径。
        /// </summary>
        /// <param name="sourceDeviceId">触发信号的设备（日志定位用）。</param>
        public async Task SignalConnectionFailureAsync(int sourceDeviceId)
        {
            Interlocked.Increment(ref _failureSignals);
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
            {
                return; // 已有重建在途：去重（多设备并发触发收敛为一次）。
            }

            try
            {
                var driver = _driver;
                if (driver != null)
                {
                    bool alive;
                    try { alive = await driver.IsAliveAsync(); }
                    catch { alive = false; }

                    if (alive)
                    {
                        Interlocked.Exchange(ref _failureSignals, 0);
                        _logger.LogWarning(
                            "连接 {ConnKey}(#{ConnId}) 收到设备 {DeviceId} 断线信号，但探测连接存活，判定为设备级故障，不重建会话。",
                            Key, ConnectionId, sourceDeviceId);
                        return;
                    }
                }

                await ReconnectCoreAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        }

        /// <summary>会话级重连（闸门保护，至少一次成功才返回 true；返回 false 表示重建在途未执行）。</summary>
        public async Task<bool> ReconnectAsync()
        {
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
            {
                return false;
            }

            try
            {
                return await ReconnectCoreAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        }

        /// <summary>
        /// 会话重建（顺序为硬约束，D5）：
        /// 1. 扇出 Error + 取消挂载 Worker 并等待收尾（杜绝"Worker 用着旧驱动时被脚下拆除"）；
        /// 2. Dispose 旧驱动；
        /// 3. 新驱动建连（连接参数从 _connection 取，配置热更新后即新值）；
        /// 4. 换驱动 + 扇出 Connected + 复位各设备计数（设备运行时不重建）。
        /// </summary>
        private async Task<bool> ReconnectCoreAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return false;

                var devices = _mounted.Values.ToList();

                // 1) 扇出 Error + 取消挂载 Worker 并等待收尾。
                State = DeviceConnectionState.Error;
                foreach (var rt in devices)
                {
                    rt.NeedsReconnect = true;
                    rt.CancelWorker();
                }
                foreach (var rt in devices)
                {
                    if (rt.WorkerTask != null)
                    {
                        try
                        {
                            await rt.WorkerTask.WaitAsync(TimeSpan.FromSeconds(3));
                        }
                        catch (TimeoutException)
                        {
                            _logger.LogWarning("连接 {ConnKey}(#{ConnId}) 设备 {DeviceKey} Worker 停止超时（3s），可能仍在退出。",
                                Key, ConnectionId, rt.Device.Key);
                        }
                    }
                }

                // 2) Dispose 旧驱动。
                var old = _driver;
                _driver = null;
                await SafeDisposeDriverAsync(old);

                // 3) 新驱动建连。
                var driver = _driverFactory.CreateDriver(_protocolKey);
                bool ok;
                try
                {
                    ok = await driver.ConnectAsync(this);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "连接 {ConnKey}(#{ConnId}) 重连建连异常。", Key, ConnectionId);
                    await SafeDisposeDriverAsync(driver);
                    State = DeviceConnectionState.Error;
                    return false;
                }

                if (!ok)
                {
                    await SafeDisposeDriverAsync(driver);
                    State = DeviceConnectionState.Error;
                    return false;
                }

                // 4) 换驱动 + 扇出 + 复位（设备运行时不重建 → 变量值/报警计数/RunState 存活）。
                _driver = driver;
                Interlocked.Exchange(ref _failureSignals, 0);
                foreach (var rt in devices)
                {
                    rt.ConsecutiveFailureCount = 0;
                    rt.NeedsReconnect = false;
                }
                State = DeviceConnectionState.Connected;

                // 订阅全量重订（Step 4.2）：新驱动订阅为空——必须清空路由表，否则差量同步会
                // 误判"已订阅"而漏订（P1 类隐蔽缺陷）。ScheduleSync 为 fire-and-forget 去抖入口，
                // 实际同步在锁外执行（重订在锁外，P2-7 纪律）；同步失败走 4.1 退避重试路径。
                _subscriptionRoutes.Clear();
                ScheduleSync();
                return true;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>销毁会话：置终态、释放驱动、解绑全部挂载设备。幂等。</summary>
        public async ValueTask DisposeAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return;
                _disposed = true;
                State = DeviceConnectionState.Disconnected;
                // 路由表整体清空：驱动 Dispose 已清空订阅集合；清空路由使后续 ScheduleSync guard 生效（no-op），
                // 且避免残留路由在下次差量同步时误判"已订阅"（会话已销毁，正常不会有下次）。
                _subscriptionRoutes.Clear();
                var driver = _driver;
                _driver = null;
                await SafeDisposeDriverAsync(driver);
                foreach (var rt in _mounted.Values)
                {
                    rt.Session = null;
                }
                _mounted.Clear();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private async Task SafeDisposeDriverAsync(IProtocolDriver? driver)
        {
            if (driver == null) return;
            try
            {
                await driver.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "连接 {ConnKey}(#{ConnId}) 驱动释放异常（已忽略）。", Key, ConnectionId);
            }
        }
    }
}