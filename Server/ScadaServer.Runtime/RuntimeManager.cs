using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Interfaces;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Runtime.Devices;
using ScadaServer.Runtime.Interface;

namespace ScadaServer.Runtime
{
    /// <summary>
    /// SCADA运行时管理器，负责管理所有设备运行时的生命周期
    /// </summary>
    public class RuntimeManager : IRuntimeManager
    {
        /// <summary>
        /// 设备运行时字典，键为设备ID
        /// </summary>
        public ConcurrentDictionary<int, Devices.DeviceRuntime> DeviceRuntimes { get; } = new();

        private readonly ILogger<RuntimeManager> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IProtocolDriverFactory _driverFactory;
        private readonly DeviceRegistry _deviceRegistry;
        private readonly IScadaNotificationService _notificationService;
        private readonly IServiceScopeFactory _scopeFactory;
        private DeviceScheduler? _scheduler;

        /// <summary>
        /// 各设备上一次对外推送/持久化的状态，用于去重，避免重复触发 StatusChanged。
        /// </summary>
        private readonly ConcurrentDictionary<int, DeviceStatus> _lastPushedStatus = new();

        /// <summary>
        /// 设备运行时状态变更事件（仅在对外状态值变化时触发）。
        /// </summary>
        public event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// 初始化运行时管理器
        /// </summary>
        public RuntimeManager(
            ILogger<RuntimeManager> logger,
            ILoggerFactory loggerFactory,
            IProtocolDriverFactory driverFactory,
            DeviceRegistry deviceRegistry,
            IScadaNotificationService notificationService,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _driverFactory = driverFactory;
            _deviceRegistry = deviceRegistry;
            _notificationService = notificationService;
            _scopeFactory = scopeFactory;
        }

        /// <inheritdoc/>
        public void RegisterDevice(Devices.DeviceRuntime runtime)
        {
            DeviceRuntimes[runtime.Device.Id] = runtime;
            runtime.ConnectionStateChanged += OnDeviceConnectionStateChanged;

            // 初始化首轮状态，确保订阅者能拿到初始态（如初始化失败导致的 Offline）。
            var initial = MapConnectionStateToStatus(runtime);
            _lastPushedStatus[runtime.Device.Id] = initial;
        }

        private async void OnDeviceConnectionStateChanged(int deviceId, DeviceConnectionState state)
        {
            if (!DeviceRuntimes.TryGetValue(deviceId, out var runtime))
            {
                return;
            }

            var status = MapConnectionStateToStatus(runtime);

            // 仅在对外状态值变化时触发，抑制抖动（连接态频繁来回切换但对外语义不变）。
            if (_lastPushedStatus.TryGetValue(deviceId, out var previous) && previous == status)
            {
                return;
            }

            _lastPushedStatus[deviceId] = status;
            StatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs
            {
                DeviceId = deviceId,
                Status = status
            });

            // 主动推送设备状态变更：RuntimeManager 直接调用通知服务，
            // 避免通知服务反向注入 IRuntimeManager 形成 Singleton 循环依赖。
            // 推送失败仅告警，不影响采集循环与事件订阅者（如持久化订阅者）。
            try
            {
                await _notificationService.NotifyDeviceStatusAsync(deviceId, status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设备 {DeviceId} 状态变更通知推送失败。", deviceId);
            }
        }

        /// <inheritdoc/>
        public async Task InitializeAsync()
        {
            _logger.LogInformation("SCADA 运行时初始化：加载已启用设备...");

            // 仓储注册为 Scoped，而 RuntimeManager 为 Singleton。
            // 在此创建独立 Scope 解析 DbContext，以便通过 Include 一次性加载运行期所需的完整对象图。
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ScadaDbContext>();

            // 加载所有启用设备及其完整运行期依赖：
            // Device → DataModel(→Protocol) → DeviceConfig → DeviceVariable(→ModelVariable)
            // 运行时仅消费新模型；严禁直接访问 ModelVariable.Address，地址由 DeviceVariable 提供。
            var devices = await db.Devices
                .Include(d => d.Model).ThenInclude(m => m.Protocol)
                .Include(d => d.Config)
                .Include(d => d.DeviceVariables).ThenInclude(dv => dv.ModelVariable)
                .Where(d => d.IsEnabled)
                .ToListAsync();

            _logger.LogInformation("找到 {Count} 个已启用设备。", devices.Count);

            foreach (var device in devices)
            {
                try
                {
                    var model = device.Model;
                    if (model == null)
                    {
                        _logger.LogWarning("设备 {Key} 缺少关联数据模型，已跳过。", device.Key);
                        continue;
                    }

                    // 协议真相源优先取 DataModel.Protocol.DriverKey；回退到过渡字段 Model.Type
                    var driverKey = model.Protocol?.DriverKey;
                    var protocolLabel = driverKey ?? model.Type.ToString();
                    var driver = string.IsNullOrWhiteSpace(driverKey)
                        ? _driverFactory.CreateDriver(model.Type)
                        : _driverFactory.CreateDriver(driverKey);

                    // 先构建运行时对象（Driver 待连接成功后赋值），再以 IRuntimeDevice 只读视图连接驱动。
                    // 第九阶段起：驱动只接收 RuntimeDevice / RuntimeVariable，不再感知 Device / DataModel / ModelVariable。
                    var runtime = new Devices.DeviceRuntime(device)
                    {
                        Device = device,
                        Model = model,
                        Protocol = model.Protocol,
                        Config = device.Config,
                        Area = device.Area
                    };

                    var connected = await driver.ConnectAsync(runtime);
                    if (!connected)
                    {
                        _logger.LogWarning("设备 {Key} ({Protocol}) 连接失败，已跳过。", device.Key, protocolLabel);
                        await driver.DisposeAsync();
                        continue;
                    }

                    runtime.Driver = driver;

                    var now = DateTime.Now;
                    foreach (var dv in device.DeviceVariables ?? Enumerable.Empty<DeviceVariable>())
                    {
                        if (dv.ModelVariable == null)
                        {
                            _logger.LogWarning(
                                "设备 {Key} 的设备变量 #{DvId} 缺少关联模型变量，已跳过。", device.Key, dv.Id);
                            continue;
                        }

                        // 构建 RuntimeVariable：变量定义来自 ModelVariable，设备配置来自 DeviceVariable。
                        runtime.Variables[dv.Id] = new VariableRuntime
                        {
                            Definition = dv.ModelVariable,
                            Instance = dv,
                            NextPollTime = now // 首轮立即采集
                        };
                    }

                    RegisterDevice(runtime);

                    _deviceRegistry.UpdateDevice(device,
                        (device.DeviceVariables ?? Enumerable.Empty<DeviceVariable>())
                            .Select(dv => dv.ModelVariable)
                            .Where(mv => mv != null)
                            .Cast<ModelVariable>()
                            .ToList());

                    _logger.LogInformation(
                        "设备 {Key} ({Protocol}) 初始化完成，共 {VarCount} 个变量。",
                        device.Key, protocolLabel, runtime.Variables.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "设备 {Key} 初始化失败。", device.Key);
                }
            }
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken token, int maxConcurrentWorkers = 10)
        {
            if (_scheduler != null) return;

            _scheduler = new DeviceScheduler(
                this,
                maxConcurrentWorkers,
                _loggerFactory.CreateLogger<DeviceScheduler>(),
                _loggerFactory.CreateLogger<DeviceWorker>(),
                _notificationService);

            await _scheduler.StartAsync(token);
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            if (_scheduler != null)
            {
                var scheduler = _scheduler;
                _scheduler = null;

                await scheduler.StopAsync();
            }

            // 停止轮询后，断开所有设备驱动（如 S7 PLC 连接）并释放其资源，确保优雅关闭。
            foreach (var runtime in DeviceRuntimes.Values)
            {
                try
                {
                    await runtime.Driver.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "设备 {Key} 驱动断开连接时发生异常（已忽略）。", runtime.Device.Key);
                }
            }
        }

        /// <inheritdoc/>
        public bool TryGetRuntimeStatus(int deviceId, out DeviceStatus status)
        {
            if (DeviceRuntimes.TryGetValue(deviceId, out var runtime))
            {
                status = MapConnectionStateToStatus(runtime);
                return true;
            }

            status = DeviceStatus.Offline;
            return false;
        }

        /// <summary>
        /// 将设备运行时的连接态与运行态映射为对外设备状态枚举。
        /// 注意：此处仅依据运行时内存态，不依赖数据库持久字段。
        /// </summary>
        private static DeviceStatus MapConnectionStateToStatus(Devices.DeviceRuntime runtime)
        {
            if (!runtime.Device.IsEnabled)
            {
                return DeviceStatus.Offline;
            }

            return runtime.ConnectionState switch
            {
                DeviceConnectionState.Connected => DeviceStatus.Online,
                DeviceConnectionState.Connecting or DeviceConnectionState.Initializing => DeviceStatus.Connecting,
                DeviceConnectionState.Error => DeviceStatus.Fault,
                DeviceConnectionState.Disconnected or DeviceConnectionState.Unknown => DeviceStatus.Offline,
                _ => DeviceStatus.Offline
            };
        }
    }
}
