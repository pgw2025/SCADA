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
    private readonly DatabaseInitializationStatus _dbReady;

    /// <summary>
    /// 初始化运行时托管服务
    /// </summary>
    /// <param name="runtimeManager">运行时管理器</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="dbReady">数据库初始化就绪协调服务（等待数据库迁移完成后再查询）</param>
    public RuntimeHostedService(
        RuntimeManager runtimeManager,
        ILogger<RuntimeHostedService> logger,
        DatabaseInitializationStatus dbReady)
    {
        _runtimeManager = runtimeManager;
        _logger = logger;
        _dbReady = dbReady;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("SCADA Runtime Starting...");

        // 等待数据库初始化（迁移 + 种子）完成，避免 Runtime 早于数据库就绪而查询缺失表。
        // 不依赖 Thread.Sleep，而是基于 TaskCompletionSource 阻塞等待。
        InitializationResult dbResult;
        try
        {
            dbResult = await _dbReady.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 等待期间应用关闭：正常生命周期结束，不视为失败。
            _logger.LogInformation("SCADA Runtime 在等待数据库初始化时被取消（应用正在关闭）。");
            return;
        }

        if (!dbResult.Succeeded)
        {
            // 数据库初始化失败或被取消：不抛异常（避免 BackgroundServiceExceptionBehavior=StopHost
            // 触发整个 Host 停止并引发 EventLog disposed 二次异常），仅记录并安全退出本后台任务。
            if (dbResult.IsCancelled)
            {
                _logger.LogWarning("数据库初始化被取消（应用正在关闭），SCADA Runtime 跳过启动。");
            }
            else
            {
                _logger.LogError(dbResult.Error,
                    "数据库初始化失败，SCADA Runtime 跳过启动（宿主将继续运行，请排查数据库问题后重启）。");
            }

            // 资源已无需分配；显式触发停止清理以正确释放可能半初始化的运行时。
            await SafeStopAsync();
            return;
        }

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
            // 初始化/启动失败：记录完整日志但不二次抛出异常，避免 StopHost 行为终止整个宿主
            // 并引发 EventLog disposed 二次异常。宿主继续运行，运维可据日志诊断后重启。
            _logger.LogError(ex,
                "SCADA Runtime 初始化/启动失败（宿主继续运行，请排查后重启服务）。");

            // 正确释放资源：清理可能半初始化的运行时，避免句柄/Driver 泄漏。
            await SafeStopAsync();
        }
    }

    /// <summary>
    /// 安全执行运行时停止清理，吞掉内部异常以免在异常处理路径上产生二次异常。
    /// </summary>
    private async Task SafeStopAsync()
    {
        try
        {
            await _runtimeManager.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SCADA Runtime 清理停止时发生异常（已忽略）。");
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