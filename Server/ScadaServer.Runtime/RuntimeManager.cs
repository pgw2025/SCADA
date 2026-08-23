using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Communication;
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
        }

        /// <inheritdoc/>
        public async Task InitializeAsync()
        {
            _logger.LogInformation("SCADA 运行时初始化：加载已启用设备...");

            // 仓储注册为 Scoped，而 RuntimeManager 为 Singleton。
            // 在此创建独立 Scope 解析仓储，避免从根容器捕获 Scoped 服务。
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var deviceRepo = sp.GetRequiredService<IDeviceRepository>();
            var configRepo = sp.GetRequiredService<IRepository<DeviceConfig, int>>();
            var modelRepo = sp.GetRequiredService<IDataModelRepository>();
            var variableRepo = sp.GetRequiredService<IModelVariableRepository>();

            var devices = await deviceRepo.GetListAsync(d => d.IsEnabled);
            _logger.LogInformation("找到 {Count} 个已启用设备。", devices.Count);

            foreach (var device in devices)
            {
                try
                {
                    var driver = _driverFactory.CreateDriver(device.Type);

                    var config = await configRepo.GetByIdAsync(device.Id);
                    var model = await modelRepo.GetByIdAsync(device.ModelId);
                    var variables = await variableRepo.GetListAsync(v => v.ModelId == device.ModelId);

                    var connected = await driver.ConnectAsync(device, config?.JsonConfig ?? "{}");
                    if (!connected)
                    {
                        _logger.LogWarning("设备 {Key} ({Type}) 连接失败，已跳过。", device.Key, device.Type);
                        await driver.DisposeAsync();
                        continue;
                    }

                    var runtime = new Devices.DeviceRuntime(device)
                    {
                        Device = device,
                        Model = model,
                        Driver = driver
                    };

                    foreach (var variable in variables)
                    {
                        runtime.Variables[variable.Id] = new VariableRuntime { Variable = variable };
                    }

                    RegisterDevice(runtime);
                    _deviceRegistry.UpdateDevice(device, variables.ToList());

                    _logger.LogInformation(
                        "设备 {Key} ({Type}) 初始化完成，共 {VarCount} 个变量。",
                        device.Key, device.Type, variables.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "设备 {Key} ({Type}) 初始化失败。", device.Key, device.Type);
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
    }
}
