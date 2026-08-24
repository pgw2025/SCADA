using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.WebApi.HostedServices;

/// <summary>
/// 启动初始化托管服务：将原本阻塞式启动初始化迁移到 Host 生命周期中执行。
/// 数据库迁移与核心配置加载为“必须成功”项，失败将导致宿主启动失败并触发优雅关闭；
/// MQTT 启动为“允许失败”项，失败仅记录日志，由 MqttManager 内部机制自动重连。
/// PLC Runtime 与后台轮询任务由既有的 RuntimeHostedService（BackgroundService）承载，
/// 其对设备级连接失败已做容错处理（跳过失败设备并记录日志）。
/// </summary>
public class StartupHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupHostedService> _logger;
    private readonly DatabaseInitializationStatus _dbReady;

    /// <summary>
    /// 初始化启动托管服务
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="dbReady">数据库初始化就绪协调服务（供 RuntimeHostedService 等待）</param>
    public StartupHostedService(
        IServiceProvider serviceProvider,
        ILogger<StartupHostedService> logger,
        DatabaseInitializationStatus dbReady)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dbReady = dbReady;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 1. 必须成功：数据库迁移 + 种子数据。
        //    失败时抛出异常，宿主启动流程会随之失败并触发优雅关闭，避免带着残缺数据库运行。
        _logger.LogInformation("开始执行启动初始化（数据库迁移与种子数据）...");
        using (var scope = _serviceProvider.CreateScope())
        {
            try
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
                await initializer.InitializeAsync(cancellationToken);

                // 通知等待方：数据库已就绪，Runtime 可以安全查询。
                _dbReady.MarkSucceeded();
                _logger.LogInformation("数据库初始化完成。");
            }
            catch (Exception ex)
            {
                // 初始化失败：通知等待方立即得知，避免 Runtime 在残缺数据库上盲目重试查询。
                _dbReady.MarkFailed(ex);
                _logger.LogError(ex, "数据库初始化失败。");
                throw;
            }
        }

        // 2. 允许失败：MQTT 启动。
        //    MqttManager.ReloadAsync 内部已对单台服务器连接失败做容错（记录日志并跳过），
        //    此处再兜底一层，确保 MQTT 故障不会阻断宿主启动，服务可继续运行并自动重连。
        try
        {
            var mqttManager = _serviceProvider.GetRequiredService<IMqttManager>();
            await mqttManager.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT 启动失败（服务将继续运行，依赖自动重连）。");
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // MQTT 客户端在宿主关闭时由 DI 容器释放（MqttManager 实现 IAsyncDisposable），
        // 该时机晚于所有托管服务的 StopAsync，可确保“先停轮询/断开 S7、再停 MQTT”的关闭顺序。
        return Task.CompletedTask;
    }
}
