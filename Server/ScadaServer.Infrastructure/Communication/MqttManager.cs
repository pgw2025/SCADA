using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ScadaServer.Infrastructure.Communication
{
    /// <summary>
    /// MQTT 连接状态（单台服务器）
    /// </summary>
    internal sealed class MqttConnectionState
    {
        public string Status { get; set; } = "Disconnected"; // Connected/Connecting/Disconnected/Error/Disabled
        public string LastError { get; set; } = string.Empty;
        public DateTime? LastConnectedUtc { get; set; }
        public int ReconnectAttempts { get; set; }
    }

    /// <summary>
    /// MQTT 管理器：维护多服务器连接、变量映射缓存与发布。
    /// 支持热加载（增删改配置后 ReloadAsync 即时生效）、断线自动重连与状态上报。
    /// </summary>
    public class MqttManager : IMqttManager, IAsyncDisposable
    {
        private readonly ILogger<MqttManager> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly MqttClientFactory _mqttFactory;

        // 服务器 ID -> MQTT 客户端
        private readonly ConcurrentDictionary<int, IMqttClient> _clients = new();
        // 服务器 ID -> 服务器配置
        private readonly ConcurrentDictionary<int, MqttServer> _serverConfigs = new();
        // 服务器 ID -> 连接状态
        private readonly ConcurrentDictionary<int, MqttConnectionState> _states = new();
        // 映射缓存: (DeviceId, VariableKey) -> List<Mapping>
        private List<MqttVariableConfig> _mappings = new();

        private readonly SemaphoreSlim _lock = new(1, 1);
        private const int ConnectTimeoutMs = 5000;

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
                foreach (var (serverId, client) in _clients)
                {
                    await SafeDisconnectAsync(serverId, client);
                    client.Dispose();
                }
                _clients.Clear();
                _serverConfigs.Clear();
                _states.Clear();
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 供 DI 容器在宿主关闭时调用：断开所有 MQTT 连接并释放内部资源。
        /// 宿主停止托管服务之后才释放单例，从而保证“先停轮询、再停 MQTT”的关闭顺序。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _lock.Dispose();
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

                // 1. 清理不再存在的服务器 / 已停用的服务器
                var activeIds = servers.Select(s => s.Id).ToHashSet();
                foreach (var serverId in _clients.Keys)
                {
                    // 服务器被删除，或已停用
                    var server = _serverConfigs.GetValueOrDefault(serverId);
                    var shouldRemove = !activeIds.Contains(serverId) || (server != null && !server.IsEnabled);
                    if (shouldRemove)
                    {
                        if (_clients.TryRemove(serverId, out var client))
                        {
                            await SafeDisconnectAsync(serverId, client);
                            client.Dispose();
                        }
                        _serverConfigs.TryRemove(serverId, out _);
                        if (server != null && !server.IsEnabled)
                        {
                            _states[serverId] = new MqttConnectionState { Status = "Disabled" };
                        }
                        else
                        {
                            _states.TryRemove(serverId, out _);
                        }
                    }
                }

                // 2. 更新或新建客户端
                foreach (var server in servers)
                {
                    _serverConfigs[server.Id] = server;
                    _states.TryAdd(server.Id, new MqttConnectionState());

                    if (!server.IsEnabled)
                    {
                        if (_clients.TryGetValue(server.Id, out var disabledClient))
                        {
                            await SafeDisconnectAsync(server.Id, disabledClient);
                            disabledClient.Dispose();
                            _clients.TryRemove(server.Id, out _);
                        }
                        _states[server.Id] = new MqttConnectionState { Status = "Disabled" };
                        continue;
                    }

                    if (!_clients.TryGetValue(server.Id, out var client))
                    {
                        client = _mqttFactory.CreateMqttClient();
                        _clients[server.Id] = client;
                    }

                    if (!client.IsConnected)
                    {
                        await ConnectClientAsync(server.Id, client, server, setState: true);
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

        /// <inheritdoc/>
        public async Task ReconnectAsync()
        {
            await _lock.WaitAsync();
            try
            {
                foreach (var server in _serverConfigs.Values)
                {
                    if (!server.IsEnabled) continue;
                    if (!_clients.TryGetValue(server.Id, out var client)) continue;
                    if (client.IsConnected) continue;

                    await ConnectClientAsync(server.Id, client, server, setState: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconnect MQTT servers");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<List<MqttServerStatusDto>> GetStatusesAsync()
        {
            // 无需持锁：仅在配置/连接变更时写入，读取近似即可。
            var result = new List<MqttServerStatusDto>();
            foreach (var server in _serverConfigs.Values)
            {
                var state = _states.GetValueOrDefault(server.Id) ?? new MqttConnectionState();
                result.Add(new MqttServerStatusDto
                {
                    Id = server.Id,
                    Name = server.Name,
                    Status = state.Status,
                    LastError = state.LastError,
                    LastConnectedUtc = state.LastConnectedUtc,
                    ReconnectAttempts = state.ReconnectAttempts,
                    VariableCount = _mappings.Count(m => m.MqttServerId == server.Id)
                });
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task<MqttTestConnectionResultDto> TestConnectionAsync(MqttServerDto dto)
        {
            var result = new MqttTestConnectionResultDto();
            var client = _mqttFactory.CreateMqttClient();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutMs));
                var options = BuildOptions(dto.BrokerUrl, dto.Port, dto.ClientId, dto.Username, dto.Password);
                await client.ConnectAsync(options, cts.Token);
                result.Success = true;
                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                client.Dispose();
            }
            return result;
        }

        private async Task ConnectClientAsync(int serverId, IMqttClient client, MqttServer server, bool setState)
        {
            var state = _states.GetOrAdd(serverId, _ => new MqttConnectionState());
            if (setState)
            {
                state.Status = "Connecting";
                state.LastError = string.Empty;
                state.ReconnectAttempts++;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutMs));
                var options = BuildOptions(server.BrokerUrl, server.Port, server.ClientId, server.Username, server.Password);
                await client.ConnectAsync(options, cts.Token);

                state.Status = "Connected";
                state.LastError = string.Empty;
                state.LastConnectedUtc = DateTime.UtcNow;
                state.ReconnectAttempts = 0;
                _logger.LogInformation($"Connected to MQTT Server: {server.Name} ({server.BrokerUrl})");
            }
            catch (Exception ex)
            {
                state.Status = "Error";
                state.LastError = ex.Message;
                _logger.LogWarning($"Failed to connect to MQTT Server {server.Name}: {ex.Message}");
            }
        }

        private static MqttClientOptions BuildOptions(string brokerUrl, int port, string clientId, string username, string? password)
        {
            return new MqttClientOptionsBuilder()
                .WithTcpServer(brokerUrl, port)
                .WithClientId(string.IsNullOrEmpty(clientId) ? Guid.NewGuid().ToString() : clientId)
                .WithCredentials(username, password)
                .WithCleanSession()
                .Build();
        }

        private async Task SafeDisconnectAsync(int serverId, IMqttClient client)
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Disconnect MQTT client failed (server {serverId})");
            }
        }

        public async Task PublishVariableUpdateAsync(int deviceId, string variableKey, object value)
        {
            var relevantMappings = _mappings.Where(m => m.DeviceId == deviceId && m.VariableKey == variableKey).ToList();
            if (relevantMappings.Count == 0) return;

            foreach (var mapping in relevantMappings)
            {
                if (!_clients.TryGetValue(mapping.MqttServerId, out var client) || !client.IsConnected)
                {
                    continue;
                }
                if (!_serverConfigs.TryGetValue(mapping.MqttServerId, out var server) || !server.IsEnabled)
                {
                    continue;
                }

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
                    var state = _states.GetOrAdd(mapping.MqttServerId, _ => new MqttConnectionState());
                    state.LastError = $"Publish failed: {ex.Message}";
                }
            }
        }
    }
}