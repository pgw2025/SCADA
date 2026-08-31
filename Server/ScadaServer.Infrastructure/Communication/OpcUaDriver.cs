using System.Text.Json;
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
    /// </summary>
    public class OpcUaDriver : IProtocolDriver
    {
        private volatile Session? _session;   // 仅在持有 _lifecycleLock 时写入；volatile 保证 KeepAlive 线程读到最新引用
        private readonly ISessionFactory _sessionFactory = new DefaultSessionFactory(DefaultTelemetry.Create(configure: _ => { }));
        private readonly Dictionary<int, Subscription> _subscriptions = new();  // 仅在持有 _lifecycleLock 时读写
        private readonly List<MonitoredItem> _monitoredItems = new();           // 仅在持有 _lifecycleLock 时读写

        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly SemaphoreSlim _ioStateLock = new(1, 1);
        private int _activeIoCount;                     // 正在使用 Session 快照的 Read/Write 数量（仅 _ioStateLock 内读写）
        private TaskCompletionSource<bool>? _drainTcs;  // 非空 = 生命周期操作正在等待 IO 排他，新的 IO 请求直接失败
        private volatile bool _disposed;

        public async Task<bool> ConnectAsync(IRuntimeDevice device)
        {
            if (_disposed) return false;
            await _lifecycleLock.WaitAsync();
            try
            {
                if (_disposed) return false;   // 等锁期间驱动可能已被 Dispose

                // 建立新连接前彻底清理旧会话与订阅状态，避免重连后残留绑定在死会话上的订阅；
                // 清理在 IO 排他保护下执行，确保没有在途 Read/Write 仍持有旧 Session
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

                // 启用 KeepAlive 断线检测：连接异常时自动触发重连
                concreteSession.KeepAliveInterval = 5000;
                concreteSession.KeepAlive += OnSessionKeepAlive;
                return true;
            }
            finally
            {
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
        /// </summary>
        private async Task CleanupConnectionAsync()
        {
            var session = _session;
            _session = null;   // 先摘引用：新的 IO 请求与 KeepAlive 身份检查立即失效

            if (session != null)
            {
                try
                {
                    session.KeepAlive -= OnSessionKeepAlive;   // 断开旧会话事件，避免清理后误触发重连
                }
                catch
                {
                    // 事件解绑失败不影响清理流程
                }
                try
                {
                    await session.CloseAsync();   // 尽力通知服务器正常关闭
                }
                catch
                {
                    // 会话已断开时 CloseAsync 抛异常属预期，忽略，继续本地清理
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
            if (_disposed) return;

            // 身份检查：只处理"当前会话"的事件；清理时 _session 先置 null，
            // 旧会话（已关闭/待释放）的残留事件在这里被直接忽略
            var current = _session;
            if (current == null || !ReferenceEquals(session, current)) return;

            // 后台重连，避免阻塞 KeepAlive 线程
            _ = Task.Run(TryReconnectAsync);
        }

        /// <summary>
        /// 自动重连：在同一会话对象上重建通道，SDK 会自动恢复（转移）原有订阅，
        /// _subscriptions / _monitoredItems 引用保持有效。
        /// </summary>
        private async Task TryReconnectAsync()
        {
            if (_disposed) return;
            // 非阻塞抢锁：已有生命周期操作（连接/断开/重连）在进行则直接退出（KeepAlive 每次触发都会进来）
            if (!await _lifecycleLock.WaitAsync(0)) return;
            try
            {
                if (_disposed) return;   // 等锁期间驱动可能已被释放

                var session = _session;
                if (session == null) return;   // 已被主动断开/清理

                // IO 排他：重连期间阻止新的 Read/Write 获取会话（通道正在重建），
                // 并等待在途 IO 结束，避免与 ReconnectAsync 竞争同一会话通道
                await BeginExclusiveIoAsync();
                try
                {
                    // 重新校验：进入排他等待期间会话可能已被断开清理
                    session = _session;
                    if (session == null) return;

                    await session.ReconnectAsync(CancellationToken.None);
                }
                finally
                {
                    await EndExclusiveIoAsync();
                }
            }
            catch
            {
                // 重连失败（服务器仍不可达等）：不做处理，
                // KeepAlive 定时器下次触发会再次进入重试
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            // 先标记：阻止新的连接/IO/订阅进入；DisconnectAsync 负责清理现存会话
            _disposed = true;
            await DisconnectAsync();
            // 与 S7Driver 约定一致：不 Dispose SemaphoreSlim，避免与并发 WaitAsync 竞态
        }
    }
}
