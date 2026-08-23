using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        public ConcurrentDictionary<int, DeviceRuntime> DeviceRuntimes { get; } = new();

        private readonly ILogger<RuntimeManager> _logger;
        private DeviceScheduler? _scheduler;

        /// <summary>
        /// 初始化运行时管理器
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public RuntimeManager(ILogger<RuntimeManager> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public void RegisterDevice(DeviceRuntime runtime)
        {
            DeviceRuntimes[runtime.Device.Id] = runtime;
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken token, int maxConcurrentWorkers)
        {
            // _scheduler = new DeviceScheduler(this, maxConcurrentWorkers, _logger);
            // await _scheduler.StartAsync(token);
        }

        /// <inheritdoc/>
        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }
    }
}