using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Interfaces;
using Opc.Ua;
using Opc.Ua.Client;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// OPC UA 协议驱动。
    ///
    /// 并发控制模型：
    /// - <see cref="_lifecycleLock"/>：互斥保护"连接生命周期与订阅集合变更"。
    ///   Connect / Disconnect / TryReconnect / Subscribe / Unsubscribe / Cleanup 之间完全互斥，
    ///   但 Read / Write 不获取该锁，避免读写被连接管理串行化。
    /// - <see cref="_ioStateLock"/>：短临界区（纯内存操作，临界区内无网络 IO），
    ///   保护 <see cref="_activeIoCount"/> / <see cref="_drainTcs"/>。
    /// - Read / Write 通过 <see cref="AcquireSessionForIoAsync"/>：在 _ioStateLock 内完成
    ///   "校验可用性 + Session 快照 + IO 引用计数 +1"，之后在锁外执行网络 IO，结束时
    ///   <see cref="ReleaseSessionIoAsync"/> 将计数 -1。计数归零前，Disconnect / Reconnect /
    ///   Cleanup 不会关闭或替换 Session —— 消除"拿到 Session 引用后被其他线程 Dispose"的窗口。
    /// - 锁顺序恒为 _lifecycleLock → _ioStateLock（单向、无环）；_ioStateLock 临界区内
    ///   从不等待 _lifecycleLock，不会死锁。
    /// - _drainTcs 使用 TaskCreationOptions.RunContinuationsAsynchronously 唤醒等待方，
    ///   避免释放 IO 计数的线程在仍持有 _ioStateLock 时被内联执行等待方延续。
    /// - 与 S7Driver 约定一致：SemaphoreSlim 从不 Dispose，避免与并发 WaitAsync 竞态。
    ///
    /// 连接状态机（<see cref="_state"/>，volatile；写操作仅发生在 _lifecycleLock 内或 Dispose 路径）：
    /// - 正常流转：Disconnected → (ConnectAsync) → Connecting → Connected
    /// - 自动重连：Connected → (KeepAlive Bad) → Reconnecting → Connected（成功）；
    ///   失败后保持 Reconnecting（保留会话、退避重试，普通 Read/Write 被状态门拒绝，不会"假连接"），
    ///   连续失败达到上限 → 释放失效会话 → Disconnected（交还运行时层重连）
    /// - 主动断开：任意状态 → (DisconnectAsync) → Disconnecting → Disconnected
    /// - 释放：任意状态 → (DisposeAsync) → Disposed（终态）
    /// - Disconnect 一旦置为 Disconnecting/Disposed，KeepAlive 回调不再启动新的后台重连任务；
    ///   已启动的任务通过 <see cref="_reconnectCts"/> 取消，并由 <see cref="_lastReconnectTask"/> 追踪，
    ///   DisposeAsync 等待其结束后才返回（不允许 Driver 释放后仍有后台任务运行）。
    /// </summary>
    public class OpcUaDriver : IProtocolDriver
    {
        /// <summary>驱动连接状态机。写操作仅在 _lifecycleLock 内或 Dispose 路径执行。</summary>
        private enum DriverState
        {
            Disconnected = 0,
            Connecting = 1,
            Connected = 2,
            Reconnecting = 3,
            Disconnecting = 4,
            Disposed = 5,
        }

        /// <summary>重连退避间隔（秒），索引 = 连续失败次数 - 1（封顶 60s）。</summary>
        private static readonly int[] ReconnectBackoffSeconds = { 5, 10, 20, 30, 60 };

        /// <summary>单次 ReconnectAsync 的超时上限，防止无限挂起占住生命周期锁。</summary>
        private static readonly TimeSpan ReconnectTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 连续自动重连失败次数达到该值后释放失效会话，交还运行时层重连。
        /// 配合退避 {5,10,20,30,60}s：约 65 秒持续不可达才放弃当前会话；
        /// 短于该时间的临时网络故障（如 30 秒断网）可在原会话上恢复、订阅由 SDK 转移不丢失。
        /// </summary>
        private const int MaxConsecutiveReconnectFailures = 5;

        private readonly ILogger<OpcUaDriver> _logger;

        private volatile Session? _session;   // 仅在持有 _lifecycleLock 时写入；volatile 保证 KeepAlive 线程读到最新引用
        private readonly ISessionFactory _sessionFactory = new DefaultSessionFactory(DefaultTelemetry.Create(configure: _ => { }));
        private readonly Dictionary<int, Subscription> _subscriptions = new();  // 仅在持有 _lifecycleLock 时读写
        private readonly List<MonitoredItem> _monitoredItems = new();           // 仅在持有 _lifecycleLock 时读写

        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly SemaphoreSlim _ioStateLock = new(1, 1);
        private int _activeIoCount;                     // 正在使用 Session 快照的 Read/Write 数量（仅 _ioStateLock 内读写）
        private TaskCompletionSource<bool>? _drainTcs;  // 非空 = 生命周期操作正在等待 IO 排他，新的 IO 请求直接失败
        private volatile bool _disposed;

        private volatile DriverState _state = DriverState.Disconnected;
        private CancellationTokenSource? _reconnectCts;             // 当前会话的后台重连取消令牌；仅 _lifecycleLock 内替换/释放，Cancel 可在锁外（线程安全且幂等）
        private volatile Task? _lastReconnectTask;                  // 最近一次后台重连任务；DisposeAsync 等待其结束
        private int _consecutiveReconnectFailures;                  // 连续重连失败计数
        private long _nextReconnectAttemptUtcTicks;                 // 下次允许触发重连的 UTC 时刻（Ticks，Interlocked 读写）

        /// <summary>
        /// 初始化驱动。注入 <see cref="ILogger{OpcUaDriver}"/> 用于记录自动重连失败/恢复与
        /// 断开清理日志（不允许吞掉异常导致现场问题无法诊断）。
        /// </summary>
        public OpcUaDriver(ILogger<OpcUaDriver> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ConnectAsync(IRuntimeDevice device)
        {
            if (_disposed) return false;
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return false;   // 等锁期间驱动可能已被 Dispose
                _state = DriverState.Connecting;

                // 建立新连接前彻底清理旧会话与订阅状态（含取消旧的后台重连令牌），
                // 避免重连后残留绑定在死会话上的订阅；
                // 清理在 IO 排他保护下执行，确保没有在途 Read/Write 仍持有旧 Session，
                // 且清理本身持有 _lifecycleLock，与旧会话上的自动 Reconnect 互斥
                await CleanupUnderExclusiveIoAsync();

                // 从 JSON 反序列化配置（配置来自 RuntimeDevice.ConfigJson，驱动不感知 Device 实体）
                var config = JsonSerializer.Deserialize<OpcUaConfig>(device.ConfigJson);
                if (config == null)
                {
                    throw new ArgumentException("无效的 OPC UA 协议配置");
                }

                var endpointUrl = config.EndpointUrl;
                if (!endpointUrl.StartsWith("opc.tcp://"))
                {
                    endpointUrl = $"opc.tcp://{endpointUrl}";
                }

                var appConfig = new ApplicationConfiguration()
                {
                    ApplicationName = "ScadaServer",
                    ApplicationType = ApplicationType.Client,
                    ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
                    SecurityConfiguration = new SecurityConfiguration { AutoAcceptUntrustedCertificates = true }
                };

                EndpointDescription selectedEndpoint;
                using (var discoveryClient = await DiscoveryClient.CreateAsync(appConfig, new Uri(endpointUrl)))
                {
                    var endpoints = await discoveryClient.GetEndpointsAsync(null, CancellationToken.None);

                    // 根据配置的安全策略选择端点
                    if (config.SecurityPolicy?.Equals("None", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        selectedEndpoint = endpoints.FirstOrDefault(e => e.SecurityMode == MessageSecurityMode.None) ?? endpoints.FirstOrDefault();
                    }
                    else
                    {
                        selectedEndpoint = endpoints.FirstOrDefault() ?? throw new Exception("未找到可用的 OPC UA 端点");
                    }
                }

                if (selectedEndpoint == null) return false;

                var endpointConfiguration = EndpointConfiguration.Create(appConfig);
                var managedEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);

                // 支持用户名密码认证
                IUserIdentity identity = null;
                if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
                {
                    identity = new UserIdentity(config.Username, System.Text.Encoding.UTF8.GetBytes(config.Password));
                }

                var session = await _sessionFactory.CreateAsync(
                    appConfig, managedEndpoint, false, "ScadaServer", 60000, identity, new List<string>());

                if (session is not Session concreteSession)
                {
                    session.Dispose();          // 转型失败也不能泄漏刚创建的会话（尚未发布，无并发使用风险）
                    return false;
                }

                if (!concreteSession.Connected)
                {
                    concreteSession.Dispose();  // 失败路径不留脏状态（尚未发布到 _session，直接释放即可）
                    return false;
                }

                // 发布会话：_session 写入只发生在 _lifecycleLock 内，
                // 与 Disconnect / Reconnect 对该字段的读取/替换互斥
                _session = concreteSession;

                // 新会话配套新的重连取消令牌（旧的已在 CleanupUnderExclusiveIoAsync 中 Cancel+Dispose）
                _reconnectCts = new CancellationTokenSource();
                _consecutiveReconnectFailures = 0;
                Interlocked.Exchange(ref _nextReconnectAttemptUtcTicks, 0);

                // 启用 KeepAlive 断线检测：连接异常时自动触发重连
                concreteSession.KeepAliveInterval = 5000;
                concreteSession.KeepAlive += OnSessionKeepAlive;

                _state = DriverState.Connected;
                // "会话已建立/重建"日志：初次连接与运行时层重连（旧会话已由上方清理路径关闭释放）均走此路径；
                // 仅记录状态，不打印端点配置等可能含凭据的信息
                _logger.LogInformation("OPC UA 会话已建立（状态：Connected）。");
                return true;
            }
            finally
            {
                // 异常/失败路径统一回落到 Disconnected，避免状态机卡在 Connecting
                if (_state == DriverState.Connecting)
                {
                    _state = _disposed ? DriverState.Disposed : DriverState.Disconnected;
                }
                _lifecycleLock.Release();
            }
        }

        public async Task<object?> ReadAsync(IRuntimeVariable variable)
        {
            // 获取 Session 快照并登记 IO 引用计数；不可用（未连接/正在断开或重连/已释放）返回 null
            var session = await AcquireSessionForIoAsync();
            if (session == null) return null;

            try
            {
                // 节点地址来源：RuntimeVariable.Address（DeviceVariable.Address）
                // IO 引用计数归零前 Session 不会被关闭或替换，此处使用是安全的
                var result = await session.ReadValueAsync(variable.Address);
                return result.Value;
            }
            finally
            {
                await ReleaseSessionIoAsync();
            }
        }

        public async Task WriteAsync(IRuntimeVariable variable, object value)
        {
            if (string.IsNullOrWhiteSpace(variable.Address))
                throw new InvalidOperationException("OPC UA 会话未连接或变量地址为空");

            var session = await AcquireSessionForIoAsync();
            if (session == null)
                throw new InvalidOperationException("OPC UA 会话未连接或变量地址为空");

            try
            {
                var node = new WriteValue
                {
                    NodeId = variable.Address,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(value))
                };

                var response = await session.WriteAsync(null, new WriteValueCollection { node }, default);
                if (response.Results.Count == 0 || response.Results[0].Code != (uint)Opc.Ua.StatusCodes.Good)
                {
                    // 用友好文案替代状态码，便于上层直接展示失败原因
                    var code = response.Results.Count > 0 ? response.Results[0].Code : (uint)0;
                    throw new InvalidOperationException($"OPC UA 写入被拒绝: 状态码 0x{code:X8}");
                }
            }
            finally
            {
                await ReleaseSessionIoAsync();
            }
        }

        public async Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<IRuntimeVariable> variables)
        {
            var results = new Dictionary<string, object>();
            var session = await AcquireSessionForIoAsync();
            if (session == null) return results;

            try
            {
                var nodesToRead = new ReadValueIdCollection(
                    variables.Select(v => new ReadValueId
                    {
                        NodeId = v.Address,
                        AttributeId = Attributes.Value
                    }));

                var response = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    default);

                var values = response.Results;

                int i = 0;
                foreach (var variable in variables)
                {
                    if (i < values.Count)
                    {
                        results[variable.Key] = values[i].Value;
                    }
                    i++;
                }
            }
            finally
            {
                await ReleaseSessionIoAsync();
            }

            return results;
        }

        public async Task SubscribeAsync(IEnumerable<IRuntimeVariable> variables, Action<string, object> onValueChanged)
        {
            if (_disposed) return;
            await _lifecycleLock.WaitAsync();
            try
            {
                // 状态门：仅 Connected 状态允许订阅（重连中/断开中的会话可能已失效）
                if (_state != DriverState.Connected) return;

                // 快照当前会话：持有 _lifecycleLock 期间会话不会被断开、替换或清理
                var session = _session;
                if (session == null || !session.Connected) return;

                // Group variables by PollingIntervalMs to optimize subscriptions
                var groups = variables.GroupBy(v => v.PollingIntervalMs);

                foreach (var group in groups)
                {
                    int interval = group.Key;
                    if (!_subscriptions.TryGetValue(interval, out var sub))
                    {
                        sub = new Subscription(session.DefaultSubscription)
                        {
                            PublishingInterval = interval,
                            DisplayName = $"Sub_{interval}ms"
                        };
                        session.AddSubscription(sub);
                        await sub.CreateAsync();
                        _subscriptions[interval] = sub;
                    }

                    foreach (var variable in group)
                    {
                        // 已订阅的变量直接跳过，避免重复创建 MonitoredItem 导致回调重复触发
                        if (_monitoredItems.Any(i => i.DisplayName == variable.Key)) continue;

                        var item = new MonitoredItem(sub.DefaultItem)
                        {
                            DisplayName = variable.Key,
                            StartNodeId = variable.Address,
                            SamplingInterval = interval
                        };

                        item.Notification += (m, e) =>
                        {
                            var notification = e.NotificationValue as MonitoredItemNotification;
                            if (notification != null)
                            {
                                onValueChanged(variable.Key, notification.Value.Value);
                            }
                        };

                        sub.AddItem(item);
                        _monitoredItems.Add(item);
                    }
                    await sub.ApplyChangesAsync();
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task UnsubscribeAsync(IEnumerable<IRuntimeVariable> variables)
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                // 快照当前会话，判断能否向服务器提交删除
                var session = _session;
                var serverReachable = session != null && session.Connected;

                // 记录受影响的订阅，移除 item 后统一向服务器提交变更
                var affectedSubscriptions = new HashSet<Subscription>();

                foreach (var variable in variables)
                {
                    // 全量移除同名 item，防止历史重复订阅残留
                    var items = _monitoredItems.Where(i => i.DisplayName == variable.Key).ToList();
                    foreach (var item in items)
                    {
                        var sub = item.Subscription;
                        sub?.RemoveItem(item);          // 本地移除 + 标记待服务器删除
                        _monitoredItems.Remove(item);
                        if (sub != null) affectedSubscriptions.Add(sub);
                    }
                }

                foreach (var sub in affectedSubscriptions)
                {
                    if (sub.MonitoredItemCount == 0)
                    {
                        // 订阅已空：整条删除，服务器端会一并删除其 MonitoredItem
                        if (serverReachable) await sub.DeleteAsync(true);
                        sub.Dispose();                  // 释放客户端订阅对象
                        var kv = _subscriptions.FirstOrDefault(x => x.Value == sub);
                        if (kv.Value != null) _subscriptions.Remove(kv.Key);
                    }
                    else if (serverReachable)
                    {
                        // 提交挂起的删除，让服务器真正停止推送该变量
                        await sub.ApplyChangesAsync();
                    }
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            // 立即进入 Disconnecting：从这一刻起 KeepAlive 回调不再启动新的后台重连任务
            // （即使本方法还在等待 _lifecycleLock —— 正在进行的 Reconnect 持有该锁）
            if (_state != DriverState.Disposed) _state = DriverState.Disconnecting;

            // 尽力取消在途的后台 Reconnect（CTS 的 Cancel 线程安全且幂等），
            // 让其尽快结束并释放 _lifecycleLock，缩短本方法的等待时间；
            // 即便此处与 ConnectAsync 存在交错误取消新令牌，随后持锁清理也会完整释放，不影响正确性
            CancelAndDisposeReconnectToken();

            await _lifecycleLock.WaitAsync();
            try
            {
                // IO 排他：等待所有在途 Read/Write 结束后再关闭会话，
                // 保证不会有线程仍在使用即将 Dispose 的 Session
                await CleanupUnderExclusiveIoAsync();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// 在 IO 排他保护下清理连接。调用方必须已持有 <see cref="_lifecycleLock"/>。
        /// </summary>
        private async Task CleanupUnderExclusiveIoAsync()
        {
            await BeginExclusiveIoAsync();
            try
            {
                await CleanupConnectionAsync();
            }
            finally
            {
                await EndExclusiveIoAsync();
            }
        }

        /// <summary>
        /// 异常安全地清理旧会话与订阅状态。
        /// 旧会话可能已失效（CloseAsync 会抛异常），但清理仍必须完成。
        /// 调用方必须已持有 _lifecycleLock，且已通过 BeginExclusiveIoAsync 获得 IO 排他权
        /// （此时不存在任何在途 Read/Write，可安全关闭并释放 Session）。
        /// 持有 _lifecycleLock 同时保证此刻没有后台 Reconnect 在执行（其同样需要该锁），
        /// 因此这里取消并释放重连令牌是安全的。
        /// </summary>
        private async Task CleanupConnectionAsync()
        {
            // 取消并释放当前会话的后台重连令牌（防止清理后仍有残留重连引用旧会话）
            CancelAndDisposeReconnectToken();

            var session = _session;
            _session = null;   // 先摘引用：新的 IO 请求与 KeepAlive 身份检查立即失效

            if (session != null)
            {
                _logger.LogInformation("OPC UA 会话已断开，正在关闭并释放（状态：{State}）。", _state);
                try
                {
                    session.KeepAlive -= OnSessionKeepAlive;   // 断开旧会话事件，避免清理后误触发重连
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "OPC UA 解绑 KeepAlive 事件时出现异常（不影响清理流程）。");
                }
                try
                {
                    await session.CloseAsync();   // 尽力通知服务器正常关闭
                }
                catch (Exception ex)
                {
                    // 会话已断开时 CloseAsync 抛异常属预期，记录后继续本地清理
                    _logger.LogDebug(ex, "OPC UA 关闭会话时出现异常（会话可能已断开，继续本地清理）。");
                }
                finally
                {
                    session.Dispose();
                }
            }

            // 持有 _lifecycleLock，与 Subscribe/Unsubscribe 的集合访问互斥，
            // Clear 不会与集合读写并发执行导致状态损坏
            _subscriptions.Clear();
            _monitoredItems.Clear();

            // 状态回落：若驱动已 Dispose 则保持终态 Disposed
            if (_state != DriverState.Disposed) _state = DriverState.Disconnected;
        }

        /// <summary>
        /// 取消并释放当前的后台重连取消令牌。
        /// Cancel 可在 _lifecycleLock 外调用（CTS 线程安全、幂等、Dispose 后为 no-op）；
        /// Dispose 本身仅在持锁路径（Cleanup/DisposeAsync 排空后台任务后）触发，避免与在途注册竞态。
        /// </summary>
        private void CancelAndDisposeReconnectToken()
        {
            var cts = _reconnectCts;
            _reconnectCts = null;
            if (cts == null) return;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已被释放：幂等，无需处理
            }
            cts.Dispose();
        }

        /// <summary>
        /// 获取一个可安全使用的 Session 快照并登记 IO 引用计数。
        /// 返回 null 表示当前不可用（未连接 / 正在断开或重连 / 驱动已释放）。
        /// 只要计数未归零，Disconnect/Reconnect 就不会关闭或替换该 Session。
        /// </summary>
        private async Task<Session?> AcquireSessionForIoAsync()
        {
            if (_disposed) return null;

            await _ioStateLock.WaitAsync();
            try
            {
                // 生命周期操作持有排他权（_drainTcs 非空）期间，拒绝新的 IO
                if (_drainTcs != null) return null;

                // 状态门：仅 Connected 状态允许普通读写。Reconnecting（会话已确认失效、退避重试中）、
                // Connecting/Disconnecting/Disposed 等状态一律拒绝，避免"假连接"（拿到 Session 引用但通道已断）
                if (_state != DriverState.Connected) return null;

                var session = _session;
                if (session == null || !session.Connected) return null;

                _activeIoCount++;
                return session;
            }
            finally
            {
                _ioStateLock.Release();
            }
        }

        /// <summary>
        /// 归还 IO 引用计数；计数归零时唤醒等待排他的生命周期操作。
        /// </summary>
        private async Task ReleaseSessionIoAsync()
        {
            await _ioStateLock.WaitAsync();
            try
            {
                _activeIoCount--;
                if (_activeIoCount == 0)
                {
                    // RunContinuationsAsynchronously：等待方在线程池上被唤醒，
                    // 不会内联在当前线程（当前仍持有 _ioStateLock）执行导致自等待
                    _drainTcs?.TrySetResult(true);
                }
            }
            finally
            {
                _ioStateLock.Release();
            }
        }

        /// <summary>
        /// 进入 IO 排他：阻止新的 Read/Write 获取 Session，并等待所有在途 IO 结束。
        /// 调用方必须已持有 _lifecycleLock。
        /// 等待有界（在途请求受 OPC UA 请求超时约束），不会死锁。
        /// </summary>
        private async Task BeginExclusiveIoAsync()
        {
            Task drainTask;
            await _ioStateLock.WaitAsync();
            try
            {
                _drainTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                drainTask = _drainTcs.Task;
                if (_activeIoCount == 0) _drainTcs.TrySetResult(true);
            }
            finally
            {
                _ioStateLock.Release();
            }

            // 已释放 _ioStateLock 后再等待，_ioStateLock 临界区内无任何 await 网络 IO
            await drainTask;
        }

        /// <summary>
        /// 退出 IO 排他：恢复 Read/Write 获取 Session 的能力。
        /// </summary>
        private async Task EndExclusiveIoAsync()
        {
            await _ioStateLock.WaitAsync();
            try
            {
                _drainTcs = null;
            }
            finally
            {
                _ioStateLock.Release();
            }
        }

        /// <summary>
        /// KeepAlive 回调：服务端无响应或状态异常时在后台触发自动重连。
        /// 注意运行在协议栈 KeepAlive 线程上，禁止阻塞或抛异常。
        /// </summary>
        private void OnSessionKeepAlive(ISession session, KeepAliveEventArgs e)
        {
            if (!ServiceResult.IsBad(e.Status)) return;
            // Connected 正常触发；Reconnecting（上次重连失败、退避等待中）也允许触发以驱动下次重试；
            // Disconnecting/Connecting/Disposed 等状态不再触发
            var triggerState = _state;
            if (_disposed || (triggerState != DriverState.Connected && triggerState != DriverState.Reconnecting)) return;

            // 身份检查：只处理"当前会话"的事件；清理时 _session 先置 null，
            // 旧会话（已关闭/待释放）的残留事件在这里被直接忽略，不可能影响新会话
            var current = _session;
            if (current == null || !ReferenceEquals(session, current)) return;

            // 失败退避：仍在退避窗口内则跳过，避免 KeepAlive 高频触发造成 CPU/网络压力
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _nextReconnectAttemptUtcTicks)) return;

            var cts = _reconnectCts;
            if (cts == null) return;   // 令牌已被清理（断开中），不启动新任务

            // 后台重连：任务被 _lastReconnectTask 追踪（DisposeAsync 等待其结束），
            // 令牌在启动前物化为快照，避免闭包执行时 CTS 已被释放；
            // 传入的必须是已通过身份检查的 current（Session），而非回调参数（ISession）
            var token = cts.Token;
            _lastReconnectTask = Task.Run(() => TryReconnectAsync(current, token));
        }

        /// <summary>
        /// 自动重连：在同一会话对象上重建通道，SDK 会自动恢复（转移）原有订阅，
        /// _subscriptions / _monitoredItems 引用保持有效。
        /// 与 Disconnect/Connect 通过 _lifecycleLock 互斥；ReconnectAsync 可被取消且带超时，
        /// 不会无限占住锁。
        /// </summary>
        private async Task TryReconnectAsync(Session expectedSession, CancellationToken ct)
        {
            if (_disposed) return;
            // 非阻塞抢锁：已有生命周期操作（连接/断开/重连）在进行则直接退出
            // （多个 KeepAlive 触发的并发任务在此收敛为单实例）
            if (!await _lifecycleLock.WaitAsync(0)) return;
            try
            {
                if (_disposed) return;                       // 等锁期间驱动可能已被释放
                if (_state is not (DriverState.Connected or DriverState.Reconnecting)) return;   // Disconnecting/Disposed 等状态：禁止自动重连

                var session = _session;
                if (session == null || !ReferenceEquals(session, expectedSession)) return;   // 会话已被替换/清理

                _state = DriverState.Reconnecting;

                await BeginExclusiveIoAsync();
                try
                {
                    // 重新校验：等待 IO 排空期间会话可能已被断开清理（此时 _session == null，
                    // 后续状态由清理方决定），或驱动已释放
                    session = _session;
                    if (session == null || !ReferenceEquals(session, expectedSession) ||
                        _state != DriverState.Reconnecting || _disposed)
                    {
                        return;
                    }

                    // 链接令牌：Disconnect/Dispose/Cleanup 时主动取消以打断在途 Reconnect；
                    // 30s 超时防止 ReconnectAsync 无限挂起并占住 _lifecycleLock
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(ReconnectTimeout);

                    _logger.LogInformation("OPC UA 开始自动重连（第 {Attempt} 次尝试）。", _consecutiveReconnectFailures + 1);
                    try
                    {
                        await session.ReconnectAsync(timeoutCts.Token);

                        _consecutiveReconnectFailures = 0;
                        if (_state != DriverState.Disposed) _state = DriverState.Connected;   // 恢复
                        _logger.LogInformation("OPC UA 会话自动重连成功，通信已恢复。");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // 被主动取消（Disconnect/Dispose/Cleanup 打断）：预期行为，
                        // 直接终止且不计入失败；会话的关闭与状态回落由清理方在锁内完成
                        _logger.LogDebug("OPC UA 自动重连被主动断开/释放操作取消。");
                    }
                    catch (Exception ex)
                    {
                        // 重连失败或超时：记录日志（不吞异常），进入退避与失败计数
                        await NoteReconnectFailureAsync(ex);
                    }
                }
                finally
                {
                    await EndExclusiveIoAsync();
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// 记录一次重连失败并决定后续策略：退避重试，或连续失败达到上限后释放失效会话。
        /// 调用方必须已持有 <see cref="_lifecycleLock"/>。
        /// </summary>
        private async Task NoteReconnectFailureAsync(Exception ex)
        {
            _consecutiveReconnectFailures++;
            var backoffSeconds = ReconnectBackoffSeconds[Math.Min(_consecutiveReconnectFailures, ReconnectBackoffSeconds.Length) - 1];
            Interlocked.Exchange(ref _nextReconnectAttemptUtcTicks, DateTime.UtcNow.AddSeconds(backoffSeconds).Ticks);
            _logger.LogWarning(ex,
                "OPC UA 自动重连失败（连续第 {FailureCount} 次），最早 {BackoffSeconds} 秒后重试。",
                _consecutiveReconnectFailures, backoffSeconds);

            if (_consecutiveReconnectFailures >= MaxConsecutiveReconnectFailures)
            {
                // 不无限持有一个已失效的会话：释放 Session 与订阅、回到 Disconnected，
                // 后续恢复交由 Runtime 层重连机制重新走 ConnectAsync 建立全新会话。
                // 直接调用 CleanupConnectionAsync（而非 CleanupUnderExclusiveIoAsync）：
                // 调用方（重连任务）已持有 _lifecycleLock 与 IO 排他权，不可重入 BeginExclusiveIoAsync
                _logger.LogWarning(
                    "OPC UA 连续 {FailureCount} 次自动重连失败，释放失效会话，等待运行时层重连。",
                    _consecutiveReconnectFailures);
                await CleanupConnectionAsync();
                _consecutiveReconnectFailures = 0;
                Interlocked.Exchange(ref _nextReconnectAttemptUtcTicks, 0);
            }
            // 未达阈值：保持 Reconnecting 状态（会话保留、退避后由 KeepAlive 再次触发重试）。
            // 不再置回 Connected——那会让 Read/Write 误以为会话可用（"假连接"）；
            // 普通 Read/Write 由状态门拒绝，直到重连成功（→ Connected）或达到阈值（→ Disconnected）
        }

        public async ValueTask DisposeAsync()
        {
            // 先标记终态：阻止新的连接/IO/订阅/后台重连进入
            _disposed = true;
            _state = DriverState.Disposed;

            // 立即打断在途的后台 Reconnect（让其尽快释放 _lifecycleLock）
            CancelAndDisposeReconnectToken();

            // 持锁清理现存会话与订阅（会等待在途 IO 与生命周期操作结束）
            await DisconnectAsync();

            // 等待后台重连任务结束：Dispose 返回后不存在仍在运行的后台任务。
            // 此时任务必已结束或即将结束（其内部仅有的阻塞点是可被取消/超时的 ReconnectAsync），
            // 此处兜底 await 保证"Dispose 完成后无泄漏"的强保证
            var task = _lastReconnectTask;
            if (task != null)
            {
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "OPC UA 后台重连任务以异常结束。");
                }
            }
            // 与 S7Driver 约定一致：不 Dispose SemaphoreSlim，避免与并发 WaitAsync 竞态
        }
    }
}
