using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Services;
using ScadaServer.Runtime;

namespace ScadaServer.WebApi.Middlewares
{
    /// <summary>
    /// 开放 API 动态路由网关中间件。挂在 <c>app.Map("/open", ...)</c> 分支内，
    /// 根据 (请求方法, 路径) 匹配 <see cref="IExposedApiRegistry"/> 中启用的暴露接口配置，
    /// 从运行时实时读取对应设备变量的最新值并以统一 JSON 契约返回。
    /// <para>本中间件为终端处理器：命中与否都直接写响应，不会继续执行后续管道。</para>
    /// </summary>
    public class ExposedApiMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IExposedApiRegistry _registry;
        private readonly RuntimeManager _runtimeManager;
        private readonly ILogger<ExposedApiMiddleware> _logger;

        public ExposedApiMiddleware(RequestDelegate next, IExposedApiRegistry registry, RuntimeManager runtimeManager,
            ILogger<ExposedApiMiddleware> logger)
        {
            _next = next;
            _registry = registry;
            _runtimeManager = runtimeManager;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Map /open 分支已将 PathBase 置为 /open，Path 为该前缀之后的部分。
            // 拼回完整路径供展示（如 /open/tank/level）。
            var fullPath = context.Request.PathBase + context.Request.Path;
            var method = context.Request.Method;

            if (!_registry.TryMatch(method, fullPath, out var dto) || dto == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await WriteJsonAsync(context, new { success = false, message = "接口不存在或已停用", endpoint = fullPath.ToString() });
                return;
            }

            var device = _runtimeManager.DeviceRuntimes.TryGetValue(dto.DeviceId, out var runtime)
                ? runtime
                : null;

            if (device == null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await WriteJsonAsync(context, new { success = false, message = "设备未运行或未启用", endpoint = dto.RouteUrl });
                return;
            }

            var variable = device.Variables.Values.FirstOrDefault(v => v.Key == dto.ExposedKey);
            if (variable == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await WriteJsonAsync(context, new
                {
                    success = false,
                    message = $"设备 '{device.Device.Name}' 上未找到映射变量 '{dto.ExposedKey}'",
                    endpoint = dto.RouteUrl
                });
                return;
            }

            var dev = device.Device;
            context.Response.StatusCode = StatusCodes.Status200OK;
            await WriteJsonAsync(context, new
            {
                system = "SCADA Open API",
                api_name = dto.Name,
                endpoint = dto.RouteUrl,
                device = new
                {
                    id = dev.Id,
                    key = dev.Key,
                    name = dev.Name,
                    status = device.IsRunning ? "online" : "offline",
                    connectionState = device.ConnectionState.ToString(),
                    lastCommunicationTimeUtc = NormalizeUtc(device.LastCommunicationTime)
                },
                payload = new
                {
                    variable_key = variable.Key,
                    variable_name = variable.Name,
                    unit = variable.Unit,
                    value = variable.Value,
                    quality = variable.Quality.ToString(),
                    update_time_utc = NormalizeUtc(variable.UpdateTime)
                },
                timestamp_utc = DateTime.UtcNow
            });
        }

        /// <summary>DateTime 统一以 UTC 字符串返回（项目约束：时间戳一律 UTC）。</summary>
        private static string? NormalizeUtc(DateTime? dt)
        {
            return dt?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        }

        /// <summary>以统一 JSON 结构写响应，若序列化异常则回退为纯文本 500，避免终端中间件挂起。</summary>
        private async Task WriteJsonAsync(HttpContext context, object payload)
        {
            try
            {
                await context.Response.WriteAsJsonAsync(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开放 API 响应序列化失败：{Path}", context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("{\"success\":false,\"message\":\"响应序列化失败\"}");
            }
        }
    }
}