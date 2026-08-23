using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ScadaServer.Infrastructure.Communication
{
    public class MqttManager : IMqttManager
    {
        private readonly ILogger<MqttManager> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly MqttClientFactory _mqttFactory;
        
        // 服务器 ID -> MQTT 客户端
        private readonly ConcurrentDictionary<int, IMqttClient> _clients = new();
        // 服务器 ID -> 服务器配置
        private readonly ConcurrentDictionary<int, MqttServer> _serverConfigs = new();
        // 映射缓存: (DeviceId, VariableKey) -> List<Mapping>
        private List<MqttVariableConfig> _mappings = new();
        
        private readonly SemaphoreSlim _lock = new(1, 1);

        public MqttManager(ILogger<MqttManager> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _mqttFactory = new MqttClientFactory();
        }

        public async Task StartAsync()
        {
            await ReloadAsync();
        }

        public async Task StopAsync()
        {
            await _lock.WaitAsync();
            try
            {
                foreach (var client in _clients.Values)
                {
                    if (client.IsConnected)
                    {
                        await client.DisconnectAsync();
                    }
                    client.Dispose();
                }
                _clients.Clear();
                _serverConfigs.Clear();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task ReloadAsync()
        {
            await _lock.WaitAsync();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var serverRepo = scope.ServiceProvider.GetRequiredService<IMqttServerRepository>();
                var mappingRepo = scope.ServiceProvider.GetRequiredService<IRepository<MqttVariableConfig, int>>();

                var servers = await serverRepo.GetListAsync();
                var mappings = await mappingRepo.GetListAsync(m => m.IsEnabled);

                _mappings = mappings;

                // 1. 清理不再存在的客户端
                var currentServerIds = servers.Select(s => s.Id).ToHashSet();
                foreach (var serverId in _clients.Keys)
                {
                    if (!currentServerIds.Contains(serverId))
                    {
                        if (_clients.TryRemove(serverId, out var client))
                        {
                            if (client.IsConnected) await client.DisconnectAsync();
                            client.Dispose();
                        }
                        _serverConfigs.TryRemove(serverId, out _);
                    }
                }

                // 2. 更新或新建客户端
                foreach (var server in servers)
                {
                    _serverConfigs[server.Id] = server;
                    
                    if (!_clients.TryGetValue(server.Id, out var client))
                    {
                        client = _mqttFactory.CreateMqttClient();
                        _clients[server.Id] = client;
                    }

                    if (!client.IsConnected)
                    {
                        await ConnectClientAsync(client, server);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload MQTT configurations");
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task ConnectClientAsync(IMqttClient client, MqttServer server)
        {
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(server.BrokerUrl, server.Port)
                .WithClientId(string.IsNullOrEmpty(server.ClientId) ? Guid.NewGuid().ToString() : server.ClientId)
                .WithCredentials(server.Username, server.Password)
                .WithCleanSession()
                .Build();

            try
            {
                await client.ConnectAsync(options);
                _logger.LogInformation($"Connected to MQTT Server: {server.Name} ({server.BrokerUrl})");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to connect to MQTT Server {server.Name}: {ex.Message}");
            }
        }

        public async Task PublishVariableUpdateAsync(int deviceId, string variableKey, object value)
        {
            var relevantMappings = _mappings.Where(m => m.DeviceId == deviceId && m.VariableKey == variableKey).ToList();
            if (!relevantMappings.Any()) return;

            foreach (var mapping in relevantMappings)
            {
                if (_clients.TryGetValue(mapping.MqttServerId, out var client) && client.IsConnected)
                {
                    if (_serverConfigs.TryGetValue(mapping.MqttServerId, out var server))
                    {
                        string topic = mapping.CustomTopic;
                        if (string.IsNullOrEmpty(topic))
                        {
                            string prefix = server.TopicPrefix?.TrimEnd('/') ?? "scada";
                            topic = $"{prefix}/{mapping.Alias}";
                        }

                        var payloadObj = new
                        {
                            alias = mapping.Alias,
                            originalKey = variableKey,
                            deviceId = deviceId,
                            value = value,
                            timestamp = DateTime.UtcNow
                        };

                        var message = new MqttApplicationMessageBuilder()
                            .WithTopic(topic)
                            .WithPayload(JsonSerializer.Serialize(payloadObj))
                            .Build();

                        try
                        {
                            await client.PublishAsync(message);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"Failed to publish to MQTT Server {server.Name}: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
}
