using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using ScadaServer.Application.Options;
using ScadaServer.WebApi.Extensions;
using ScadaServer.WebApi.HostedServices;

// ========== 1. 构建 Host ==========
var builder = WebApplication.CreateBuilder(args);

// 配置系统数据库选项
builder.Services.Configure<SystemDbOptions>(builder.Configuration.GetSection(SystemDbOptions.SectionName));

// 配置系统日志选项（写库门槛 / 黑名单 / 保留期）
builder.Services.Configure<SystemLogOptions>(builder.Configuration.GetSection(SystemLogOptions.SectionName));

// 将 ILogger 运行日志写入数据库的 Provider：
// 以单例注册（不经 builder.Logging.AddProvider），由 LoggerFactory 延迟解析，
// 保证依赖链（SystemLogRecorder 单例）完整后再实例化，避免 Host 构建期提前创建导致解析失败。
builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider, ScadaServer.WebApi.Logging.DatabaseLoggerProvider>();

// 供操作日志审计取当前用户/客户端 IP
builder.Services.AddHttpContextAccessor();

// 优雅关闭配置：给后台服务 30 秒完成关闭（等待 PLC 断开、MQTT 停止、后台轮询任务退出）
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);

    // 纵深防御：即使后台服务（如 RuntimeHostedService）因异常退出，也不自动终止整个 Host，
    // 避免连带触发 EventLog disposed 等二次异常。运行时初始化失败已在服务内部记录并安全释放。
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// 添加认证服务（JWT + CORS + Swagger + Controllers）
builder.Services.AddAuthenticationServices(builder.Configuration);

// 添加数据库服务（EF Core + UnitOfWork + Repositories）
builder.Services.AddDatabaseServices();

// 添加应用层服务
builder.Services.AddApplicationServices();

// 添加基础设施服务（设备注册、协议工厂、Runtime 等）
builder.Services.AddInfrastructureServices();

// 数据库初始化就绪协调服务（单例）：StartupHostedService 完成后发信号，
// RuntimeHostedService 在查询数据库前等待该信号，避免启动竞态。不使用 Thread.Sleep。
builder.Services.AddSingleton<ScadaServer.WebApi.HostedServices.DatabaseInitializationStatus>();

// 启动初始化托管服务（数据库迁移必须成功；MQTT 启动允许失败，由内部自动重连兜底）
builder.Services.AddHostedService<StartupHostedService>();

// 运行时托管服务：注册顺序在 StartupHostedService 之后（双重保险），
// 且内部仍显式 await 数据库就绪信号，确保查询前表结构已就位。
builder.Services.AddHostedService<RuntimeHostedService>();

// 系统日志自动清理托管服务（每天 3 点按分类保留期分批清理 SystemLogs）
builder.Services.AddHostedService<SystemLogCleanupHostedService>();

// 报警记录自动清理托管服务（每天 3 点按系统配置保留期分批清理 AlarmRecords）
builder.Services.AddHostedService<AlarmRecordCleanupHostedService>();

// ========== 2. 构建应用 ==========
using var app = builder.Build();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ScadaServer.Program");

// 全局异常兜底（仅作最后防线，不能替代正常异常处理与优雅关闭）
RegisterGlobalExceptionHandlers(logger);

// 配置中间件管道
app.ConfigureMiddlewarePipeline();

// ========== 3. 启动与运行 ==========
try
{
    // 端口预检：仅作友好提示，不作为最终判断；最终端口占用以 Kestrel 绑定异常为准。
    var occupied = await GetOccupiedListenPortsAsync(GetListenUrls(app.Configuration));
    if (occupied.Count > 0)
    {
        logger.LogWarning("提示：以下端口疑似已被占用（另一个 SCADA 服务实例可能正在运行），最终以 Kestrel 绑定结果为准：{Ports}", string.Join("；", occupied));
        logger.LogWarning("Windows 排查： netstat -ano | findstr :<端口>    结束进程： taskkill /PID <进程号> /F");
    }

    // 启动 Kestrel 并等待关闭信号（Ctrl+C / systemd SIGTERM / docker stop）。
    // 手动拆分 StartAsync + WaitForShutdownAsync，替代 RunAsync：
    // RunAsync 在启动异常向上传播前会先在 finally 里 Dispose 整个 Host，
    // 导致下方 catch 里 logger 已失效（Windows 上触发 EventLog disposed 二次崩溃）。
    await app.StartAsync();
    await app.WaitForShutdownAsync(
        app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);

    logger.LogInformation("SCADA 服务已正常关闭。");
    return 0;
}
catch (Exception ex) when (IsAddressInUse(ex))
{
    logger.LogCritical(ex, "错误：启动失败，端口被占用（{Address}）。请关闭占用该端口的进程后重试。", ExtractBindAddress(ex));
    await StopAppAsync(app, logger);
    return 1;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "错误：SCADA 服务启动失败。");
    await StopAppAsync(app, logger);
    return 1;
}

// ========== 4. 辅助方法 ==========

/// <summary>
/// 按优先级获取本进程将要监听的地址：ASPNETCORE_URLS → urls 配置项 → 默认 http://localhost:5000。
/// 不再依赖 launchSettings.json（发布环境不存在该文件，避免误判）。
/// </summary>
static List<string> GetListenUrls(IConfiguration configuration)
{
    var urls = new List<string>();

    // 1. ASPNETCORE_URLS 环境变量（dotnet run / systemd Environment / docker -e 注入）
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (!string.IsNullOrWhiteSpace(env))
        urls.AddRange(env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // 2. builder.Configuration["urls"]（命令行 --urls / 配置文件 urls 键等）
    if (urls.Count == 0)
    {
        var cfg = configuration["urls"];
        if (!string.IsNullOrWhiteSpace(cfg))
            urls.AddRange(cfg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    // 3. 默认端口（仅 HTTP，避免对 HTTPS/VS/IIS Express 的误检测）
    if (urls.Count == 0)
        urls.Add("http://localhost:5000");

    return urls;
}

/// <summary>
/// 异步探测监听端口是否被占用（连接成功即代表已有进程在监听）。
/// 使用 await + CancellationToken 超时，避免 Wait() 同步阻塞线程与启动竞态。
/// 仅用于友好提示，不作为启动成功的最终依据。
/// </summary>
static async Task<List<string>> GetOccupiedListenPortsAsync(IEnumerable<string> urls)
{
    var occupied = new List<string>();

    foreach (var raw in urls)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            continue;
        if (uri.Scheme is not ("http" or "https"))
            continue;

        var host = uri.Host switch
        {
            "0.0.0.0" or "*" or "+" => "127.0.0.1",
            "[::]" or "::" => "::1",
            _ => uri.Host
        };
        var port = uri.Port;
        if (port <= 0)
            continue;

        // 500ms 超时探测：连接成功即视为被占用，超时/被拒即视为空闲。
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            if (client.Connected)
                occupied.Add($"{raw}（探测地址 {host}:{port}）");
        }
        catch (OperationCanceledException)
        {
            // 探测超时 => 视为端口空闲，避免误报
        }
        catch (Exception)
        {
            // 连接失败（被拒绝）=> 端口空闲
        }
    }

    return occupied;
}

/// <summary>
/// 兜底：识别 Kestrel 绑定失败引发的“地址已被占用”异常。
/// </summary>
static bool IsAddressInUse(Exception? ex)
{
    for (var e = ex; e is not null; e = e.InnerException)
    {
        if (e is SocketException se && se.SocketErrorCode == SocketError.AddressAlreadyInUse)
            return true;
        if (e is IOException && e.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase))
            return true;
    }
    return false;
}

/// <summary>
/// 从异常信息中提取被占用的绑定地址，用于兜底提示。
/// </summary>
static string ExtractBindAddress(Exception ex)
{
    for (var e = ex; e is not null; e = e.InnerException)
    {
        var i = e.Message.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            i = e.Message.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            var k = i;
            while (k < e.Message.Length && e.Message[k] != '\r' && e.Message[k] != '\n' && e.Message[k] != ' ')
                k++;
            return e.Message.Substring(i, k - i);
        }
    }
    return "HTTP 端口";
}

/// <summary>
/// 使用 Host 停止流程优雅关闭应用（StopAsync → 停止托管服务 → 释放 DI 资源）。
/// </summary>
static async Task StopAppAsync(WebApplication app, ILogger logger)
{
    try
    {
        await app.StopAsync();
    }
    catch (Exception ex)
    {
        // 兜底路径：StopAsync 可能在 Host 已释放时被调用，此时 ILogger 可能已失效
        //（Windows EventLog provider 已 Dispose）。改用最后手段输出，避免兜底自身再抛异常。
        SafeLogError(logger, ex, "停止应用时发生异常（已忽略）。");
    }
}

/// <summary>
/// 注册全局异常兜底处理（仅作最后防线，不能替代正常的异常处理与优雅关闭）。
/// </summary>
static void RegisterGlobalExceptionHandlers(ILogger logger)
{
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
        var ex = e.ExceptionObject as Exception;
        // 兜底处理器自身绝不能抛异常，否则会覆盖原始异常、导致进程崩溃且无法定位根因。
        SafeLog(logger, () => logger.LogCritical(ex, "捕获到未处理异常（AppDomain.UnhandledException），进程即将终止。"),
            "捕获到未处理异常（AppDomain.UnhandledException）：" + ex?.Message);
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
        SafeLog(logger, () => logger.LogError(e.Exception, "捕获到未观察任务异常（TaskScheduler.UnobservedTaskException）。"),
            "捕获到未观察任务异常（TaskScheduler.UnobservedTaskException）：" + e.Exception?.Message);
        e.SetObserved();
    };
}

/// <summary>
/// 安全的日志写入：尝试通过 ILogger 输出；若 logger 已失效（例如 Host 已释放），
/// 则回退到控制台标准错误，确保兜底路径自身永不抛异常。
/// </summary>
static void SafeLog(ILogger logger, Action logAction, string fallbackMessage)
{
    try
    {
        logAction();
    }
    catch
    {
        try { Console.Error.WriteLine(fallbackMessage); }
        catch { /* 最后防线：忽略一切输出失败 */ }
    }
}

/// <summary>
/// 安全的错误日志写入（带异常对象版本，供 StopAppAsync 等兜底路径使用）。
/// </summary>
static void SafeLogError(ILogger logger, Exception ex, string message)
{
    SafeLog(logger, () => logger.LogError(ex, message), message + " " + ex.Message);
}
