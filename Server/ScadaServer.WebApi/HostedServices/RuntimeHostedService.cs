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

            await _runtimeManager.StartAsync(stoppingToken);

            _logger.LogInformation("SCADA Runtime Started");
        }
        catch (OperationCanceledException)
        {
            // 应用正在关闭时触发的取消：属于正常的生命周期结束，不视为启动失败。
            _logger.LogInformation("SCADA Runtime 在启动过程中被取消（应用正在关闭）。");
        }
        catch (Exception ex)
        {
            // 仅当初始化/启动阶段发生非取消类异常时才记为失败。
            _logger.LogError(ex, "Runtime Start Failed");
            throw;
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SCADA Runtime Stopping...");

        try
        {
            await _runtimeManager.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SCADA Runtime 停止时发生异常（已忽略）。");
        }

        await base.StopAsync(cancellationToken);
    }
}