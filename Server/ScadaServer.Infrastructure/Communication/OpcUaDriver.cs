using System.Text.Json;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Interfaces;
using Opc.Ua;
using Opc.Ua.Client;

namespace ScadaServer.Infrastructure.Communication
{
    public class OpcUaDriver : IProtocolDriver
    {
        private Session? _session;
        private readonly ISessionFactory _sessionFactory = new DefaultSessionFactory(DefaultTelemetry.Create(configure: _ => { }));
        private readonly Dictionary<int, Subscription> _subscriptions = new();
        private readonly List<MonitoredItem> _monitoredItems = new();
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);

        public async Task<bool> ConnectAsync(IRuntimeDevice device)
        {
            // 建立新连接前彻底清理旧会话与订阅状态，避免重连后残留绑定在死会话上的订阅
            await CleanupConnectionAsync();

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
                session.Dispose();          // 转型失败也不能泄漏刚创建的会话
                return false;
            }

            _session = concreteSession;
            if (!_session.Connected)
            {
                await CleanupConnectionAsync();   // 失败路径不留脏状态
                return false;
            }

            // 启用 KeepAlive 断线检测：连接异常时自动触发重连
            _session.KeepAliveInterval = 5000;
            _session.KeepAlive += OnSessionKeepAlive;
            return true;
        }

        public async Task<object?> ReadAsync(IRuntimeVariable variable)
        {
            if (_session == null || !_session.Connected) return null;
            // 节点地址来源：RuntimeVariable.Address（DeviceVariable.Address）
            var result = await _session.ReadValueAsync(variable.Address);
            return result.Value;
        }

        public async Task WriteAsync(IRuntimeVariable variable, object value)
        {
            if (_session == null || !_session.Connected || string.IsNullOrWhiteSpace(variable.Address))
                throw new InvalidOperationException("OPC UA 会话未连接或变量地址为空");

            var node = new WriteValue
            {
                NodeId = variable.Address,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(value))
            };

            var response = await _session.WriteAsync(null, new WriteValueCollection { node }, default);
            if (response.Results.Count == 0 || response.Results[0].Code != (uint)Opc.Ua.StatusCodes.Good)
            {
                // 用友好文案替代状态码，便于上层直接展示失败原因
                var code = response.Results.Count > 0 ? response.Results[0].Code : (uint)0;
                throw new InvalidOperationException($"OPC UA 写入被拒绝: 状态码 0x{code:X8}");
            }
        }

        public async Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<IRuntimeVariable> variables)
        {
            var results = new Dictionary<string, object>();
            if (_session == null || !_session.Connected) return results;

            var nodesToRead = new ReadValueIdCollection(
                variables.Select(v => new ReadValueId 
                { 
                    NodeId = v.Address, 
                    AttributeId = Attributes.Value 
                }));
            
            var response = await _session.ReadAsync(
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

            return results;
        }

        public async Task SubscribeAsync(IEnumerable<IRuntimeVariable> variables, Action<string, object> onValueChanged)
        {
            if (_session == null || !_session.Connected) return;

            // Group variables by PollingIntervalMs to optimize subscriptions
            var groups = variables.GroupBy(v => v.PollingIntervalMs);

            foreach (var group in groups)
            {
                int interval = group.Key;
                if (!_subscriptions.TryGetValue(interval, out var sub))
                {
                    sub = new Subscription(_session.DefaultSubscription)
                    {
                        PublishingInterval = interval,
                        DisplayName = $"Sub_{interval}ms"
                    };
                    _session.AddSubscription(sub);
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

        public async Task UnsubscribeAsync(IEnumerable<IRuntimeVariable> variables)
        {
            // 记录受影响的订阅，移除 item 后统一向服务器提交变更
            var affectedSubscriptions = new HashSet<Subscription>();
            var serverReachable = _session != null && _session.Connected;

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

        public async Task DisconnectAsync()
        {
            await CleanupConnectionAsync();
        }

        /// <summary>
        /// 异常安全地清理旧会话与订阅状态。
        /// 旧会话可能已失效（CloseAsync 会抛异常），但清理仍必须完成。
        /// </summary>
        private async Task CleanupConnectionAsync()
        {
            if (_session != null)
            {
                try
                {
                    await _session.CloseAsync();   // 尽力通知服务器正常关闭
                }
                catch
                {
                    // 会话已断开时 CloseAsync 抛异常属预期，忽略，继续本地清理
                }
                finally
                {
                    _session.Dispose();
                    _session = null;
                }
            }
            _subscriptions.Clear();
            _monitoredItems.Clear();
        }

        /// <summary>
        /// KeepAlive 回调：服务端无响应或状态异常时在后台触发自动重连。
        /// 注意运行在协议栈 KeepAlive 线程上，禁止阻塞或抛异常。
        /// </summary>
        private void OnSessionKeepAlive(ISession session, KeepAliveEventArgs e)
        {
            if (!ServiceResult.IsBad(e.Status)) return;
            if (_session == null || !ReferenceEquals(session, _session)) return;   // 忽略旧会话的事件

            // 后台重连，避免阻塞 KeepAlive 线程
            _ = Task.Run(TryReconnectAsync);
        }

        /// <summary>
        /// 自动重连：在同一会话对象上重建通道，SDK 会自动恢复（转移）原有订阅，
        /// _subscriptions / _monitoredItems 引用保持有效。
        /// </summary>
        private async Task TryReconnectAsync()
        {
            // 非阻塞抢锁：已有重连在进行则直接退出（KeepAlive 每次触发都会进来）
            if (!await _reconnectLock.WaitAsync(0)) return;
            try
            {
                var session = _session;
                if (session == null) return;   // 已被主动断开/清理

                await session.ReconnectAsync(CancellationToken.None);
            }
            catch
            {
                // 重连失败（服务器仍不可达等）：不做处理，
                // KeepAlive 定时器下次触发会再次进入重试
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
        }
    }
}
