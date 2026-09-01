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

        /// <summary>
        /// 启动 MQTT 管理器。作为托管服务启动入口，直接加载并建立所有启用的服务器连接。
        /// <remarks>单次加载为尽力而为：个别服务器连接失败会记录告警，不影响其余服务器，也不影响整体启动；失败恢复由重试机制承担。</remarks>
        /// </summary>
        public async Task StartAsync()
        {
            await ReloadAsync();
        }

        /// <summary>
        /// 停止 MQTT 管理器：断开所有已连接客户端并清空内部缓存（连接状态 / 服务器配置 / 客户端实例）。
        /// <remarks>持有 <see cref="_lock"/> 串行化，避免与并发 Reload/Reconnect 交错破坏内部字典。</remarks>
        /// </summary>
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

        /// <summary>
        /// 重载 MQTT 配置并同步连接状态：从数据库读取启用中的服务器与变量映射，
        /// 对已删除/停用的服务器断开清理，对启用的服务器补充或重建连接。
        /// <remarks>
        /// 热加载接口：配置增删改后随时调用即可即时生效。持有 <see cref="_lock"/> 保证
        /// 与 Stop / Reconnect 串行化；服务器连接失败仅记录告警而不回滚其余服务器。
        /// </remarks>
        /// </summary>
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

        /// <summary>
        /// 主动重连所有"已启用但当前未连接"的服务器，通常由 MQTT 重连托管服务定期调用。
        /// <remarks>持有 <see cref="_lock"/> 串行化；单个服务器重连失败不阻断其余服务器。</remarks>
        /// </summary>
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

        /// <summary>
        /// 获取全部 MQTT 服务器的连接状态快照（供前端状态面板展示）。
        /// <remarks>无需持锁：状态仅在配置/连接变更时写入，读取近似值即可。</remarks>
        /// </summary>
        /// <returns>所有已知服务器的状态 DTO 列表，含连接状态、最近错误、最近连接时间、重连次数与关联变量映射数。</returns>
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

        /// <summary>
        /// 测试一个独立 MQTT Broker 的可连接性（保存配置前的前置校验）。
        /// <remarks>创建一次性临时客户端，连上即断开，不纳入管理器连接缓存；结果仅返回成功与否及错误信息。</remarks>
        /// </summary>
        /// <param name="dto">待测试的 broker 配置（地址、端口、客户端ID、账号密码）。</param>
        /// <returns>测试结果，含是否成功与失败原因。</returns>
        public async Task<MqttTestConnectionResultDto> TestConnectionAsync(MqttTestConnectionDto dto)
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

        /// <summary>
        /// 尝试建立/恢复一个服务器客户端的连接，并同步其连接状态（连接中→已连接 或 错误）。
        /// <remarks>
        /// 连接带 5 秒超时（<see cref="ConnectTimeoutMs"/>）。<paramref name="setState"/> 用于区分
        /// 首次连接/主动重连（计入重连次数）与内部的静默恢复；失败仅置状态为 Error，不抛出。
        /// </remarks>
        /// </summary>
        /// <param name="serverId">服务器 ID。</param>
        /// <param name="client">目标 MQTT 客户端实例。</param>
        /// <param name="server">服务器配置。</param>
        /// <param name="setState">是否更新连接状态并累计重连计数。</param>
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

        /// <summary>
        /// 依据连接参数构造 MQTT 连接选项。
        /// <remarks>客户端ID为空时自动生成一个 GUID（避免多实例冲突）；使用 CleanSession 每次连接清空历史会话。</remarks>
        /// </summary>
        /// <param name="brokerUrl">Broker 主机地址。</param>
        /// <param name="port">Broker 端口。</param>
        /// <param name="clientId">客户端 ID，为空则自动生成。</param>
        /// <param name="username">账号。</param>
        /// <param name="password">密码。</param>
        /// <returns>构造完成的连接选项。</returns>
        private static MqttClientOptions BuildOptions(string brokerUrl, int port, string clientId, string username, string? password)
        {
            return new MqttClientOptionsBuilder()
                .WithTcpServer(brokerUrl, port)
                .WithClientId(string.IsNullOrEmpty(clientId) ? Guid.NewGuid().ToString() : clientId)
                .WithCredentials(username, password)
                .WithCleanSession()
                .Build();
        }

        /// <summary>
        /// 安全断开客户端：仅当已连接时才断开，并吞掉断开异常（降为 Debug），保证清理流程不被单点断开失败中断。
        /// </summary>
        /// <param name="serverId">服务器 ID（用于日志定位）。</param>
        /// <param name="client">目标客户端。</param>
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

        /// <summary>
        /// 将设备变量值变更实时发布到与之关联的所有 MQTT 映射主题（通常在变量变更总线上回调）。
        /// <remarks>
        /// 依据 <see cref="_mappings"/> 中 (DeviceId, VariableKey) 命中开启的映射，向每个目标服务器发布一条
        /// JSON 消息（含别名、原始变量键、设备ID、值、UTC 时间戳）。目标未连接/已停用时静默跳过；
        /// 发布失败不抛出，仅记录 Debug 并在该服务器连接状态上写失败原因。
        /// </remarks>
        /// </summary>
        /// <param name="deviceId">变量所属设备 ID。</param>
        /// <param name="variableKey">变量业务键。</param>
        /// <param name="value">变量当前值。</param>
        public async Task PublishVariableUpdateAsync(int deviceId, string variableKey, object value)
        {
            // 命中该设备+变量的启用映射（同一变量可映射到多个服务器/主题）
            var relevantMappings = _mappings.Where(m => m.DeviceId == deviceId && m.VariableKey == variableKey).ToList();
            if (relevantMappings.Count == 0) return;

            foreach (var mapping in relevantMappings)
            {
                // 目标服务器未运行/未连接则跳过该映射，不给上层报错
                if (!_clients.TryGetValue(mapping.MqttServerId, out var client) || !client.IsConnected)
                {
                    continue;
                }
                if (!_serverConfigs.TryGetValue(mapping.MqttServerId, out var server) || !server.IsEnabled)
                {
                    continue;
                }

                // 主题：优先用映射自定义主题；未配置则取 "服务器主题前缀/别名" 组装（前缀缺省 scada）
                string topic = mapping.CustomTopic;
                if (string.IsNullOrEmpty(topic))
                {
                    string prefix = server.TopicPrefix?.TrimEnd('/') ?? "scada";
                    topic = $"{prefix}/{mapping.Alias}";
                }

                // 构建带上下文的 JSON 负载，便于订阅方识别来源变量与时间戳
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
                    // 单点发布失败不抛出：记录 Debug 并写入状态，供状态面板/重连逻辑参考
                    _logger.LogDebug($"Failed to publish to MQTT Server {server.Name}: {ex.Message}");
                    var state = _states.GetOrAdd(mapping.MqttServerId, _ => new MqttConnectionState());
                    state.LastError = $"Publish failed: {ex.Message}";
                }
            }
        }
    }
}