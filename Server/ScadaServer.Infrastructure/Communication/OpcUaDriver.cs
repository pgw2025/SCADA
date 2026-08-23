using System.Text.Json;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces;
using Opc.Ua;
using Opc.Ua.Client;

namespace ScadaServer.Infrastructure.Communication
{
    public class OpcUaDriver : IProtocolDriver
    {
        private ISession _session;
        private readonly Dictionary<int, Subscription> _subscriptions = new();
        private readonly List<MonitoredItem> _monitoredItems = new();

        public async Task<bool> ConnectAsync(Device device, string configJson)
        {
            // 从 JSON 反序列化配置
            var config = JsonSerializer.Deserialize<OpcUaConfig>(configJson);
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
            using (var discoveryClient = DiscoveryClient.Create(new Uri(endpointUrl)))
            {
                var endpoints = discoveryClient.GetEndpoints(null);
                
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

            _session = await Session.Create(appConfig, managedEndpoint, false, "ScadaServer", 60000, identity, new List<string>());
            return _session.Connected;
        }

        public async Task<object> ReadAsync(ModelVariable variable)
        {
            if (_session == null || !_session.Connected) return null;
            var result = await _session.ReadValueAsync(variable.Address);
            return result.Value;
        }

        public async Task<IDictionary<string, object>> ReadBatchAsync(IEnumerable<ModelVariable> variables)
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

        public async Task SubscribeAsync(IEnumerable<ModelVariable> variables, Action<string, object> onValueChanged)
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

        public async Task UnsubscribeAsync(IEnumerable<ModelVariable> variables)
        {
            foreach (var variable in variables)
            {
                var item = _monitoredItems.FirstOrDefault(i => i.DisplayName == variable.Key);
                if (item != null)
                {
                    item.Subscription.RemoveItem(item);
                    _monitoredItems.Remove(item);
                }
            }

            // Cleanup empty subscriptions
            var emptyIntervals = _subscriptions.Where(s => s.Value.MonitoredItemCount == 0).Select(s => s.Key).ToList();
            foreach (var interval in emptyIntervals)
            {
                var sub = _subscriptions[interval];
                await sub.DeleteAsync(true);
                _subscriptions.Remove(interval);
            }
        }

        public async Task DisconnectAsync()
        {
            if (_session != null)
            {
                await _session.CloseAsync();
                _session.Dispose();
                _session = null;
            }
            _subscriptions.Clear();
            _monitoredItems.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
        }
    }
}
