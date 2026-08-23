using Microsoft.Extensions.Hosting;
using ScadaServer.Runtime;

namespace ScadaServer.WebApi.HostedServices;

/// <summary>
/// SCADA运行时托管服务，负责在应用启动时初始化运行时管理器
/// </summary>
public class RuntimeHostedService : BackgroundService
{
    private readonly RuntimeManager _runtimeManager;
    private readonly ILogger<RuntimeHostedService> _logger;

    /// <summary>
    /// 初始化运行时托管服务
    /// </summary>
    /// <param name="runtimeManager">运行时管理器</param>
    /// <param name="logger">日志记录器</param>
    public RuntimeHostedService(
        RuntimeManager runtimeManager,
        ILogger<RuntimeHostedService> logger)
    {
        _runtimeManager = runtimeManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("SCADA Runtime Starting...");

        try
        {
            await _runtimeManager.InitializeAsync();

            // await _runtimeManager.StartAsync(stoppingToken);

            _logger.LogInformation("SCADA Runtime Started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Runtime Start Failed");
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SCADA Runtime Stopping...");

        // await _runtimeManager.StopAsync();

        await base.StopAsync(cancellationToken);
    }
}