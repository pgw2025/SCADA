using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using ScadaServer.Application.Options;
using ScadaServer.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 配置系统数据库选项
builder.Services.Configure<SystemDbOptions>(builder.Configuration.GetSection(SystemDbOptions.SectionName));

// 添加认证服务（JWT + CORS + Swagger + Controllers）
builder.Services.AddAuthenticationServices(builder.Configuration);

// 添加数据库服务（SqlSugar + UnitOfWork + Repositories）
builder.Services.AddDatabaseServices();

// 添加应用层服务
builder.Services.AddApplicationServices();

// 添加基础设施服务（设备注册、协议工厂、Runtime等）
builder.Services.AddInfrastructureServices();

var app = builder.Build();

// 配置中间件管道
app.ConfigureMiddlewarePipeline();

// 端口预检（放在启动初始化之前，避免无谓的数据库/MQTT 连接与异常堆栈）
var occupied = GetOccupiedListenPorts(GetListenUrls());
if (occupied.Count > 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("错误：启动失败，以下端口已被占用，另一个 SCADA 服务实例可能正在运行：");
    foreach (var addr in occupied)
        Console.Error.WriteLine("   - " + addr);
    Console.ResetColor();
    Console.Error.WriteLine("请先关闭占用该端口的进程后重试。");
    Console.Error.WriteLine("Windows 排查： netstat -ano | findstr :<端口>   结束进程： taskkill /PID <进程号> /F");
    Environment.Exit(1);
}

// 执行启动初始化（数据库初始化、MQTT启动等）
await app.InitializeAsync();

try
{
    app.Run();
}
catch (Exception ex) when (IsAddressInUse(ex))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"错误：启动失败，端口被占用（{ExtractBindAddress(ex)}）。请关闭占用该端口的进程后重试。");
    Console.ResetColor();
    Environment.Exit(1);
}

// 取本进程即将监听的地址列表：优先用 ASPNETCORE_URLS（dotnet run 注入），
// 回退读取 launchSettings.json 的 applicationUrl，最后回退 ASP.NET Core 默认端口
static List<string> GetListenUrls()
{
    var urls = new List<string>();

    var env = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (!string.IsNullOrWhiteSpace(env))
        urls.AddRange(env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    if (urls.Count == 0)
    {
        var launch = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Properties", "launchSettings.json");
        if (File.Exists(launch))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(launch));
                if (doc.RootElement.TryGetProperty("profiles", out var profiles))
                {
                    foreach (var profile in profiles.EnumerateObject())
                    {
                        if (profile.Value.TryGetProperty("applicationUrl", out var au) &&
                            au.ValueKind == JsonValueKind.String)
                        {
                            foreach (var u in au.GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                if (!urls.Contains(u))
                                    urls.Add(u);
                        }
                    }
                }
            }
            catch
            {
                // 解析失败则用默认端口兜底
            }
        }
    }

    if (urls.Count == 0)
    {
        urls.Add("http://localhost:5000");
        urls.Add("https://localhost:5001");
    }

    return urls;
}

// 通过 TCP 连接探测监听端口是否被占用（连接成功即代表已有进程在监听）
static List<string> GetOccupiedListenPorts(List<string> urls)
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

        try
        {
            using var client = new TcpClient();
            if (client.ConnectAsync(host, port).Wait(500) && client.Connected)
                occupied.Add($"{raw}（探测地址 {host}:{port}）");
        }
        catch (Exception)
        {
            // 连接失败（被拒绝/超时）=> 端口空闲
        }
    }
    return occupied;
}

// 兜底：识别 Kestrel 绑定失败引发的“地址已被占用”异常
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

// 从异常信息中提取被占用的绑定地址，用于兜底提示
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
