using Microsoft.AspNetCore.Builder;
using ScadaServer.WebApi.Hubs;
using ScadaServer.WebApi.Middlewares;

namespace ScadaServer.WebApi.Extensions
{
    /// <summary>
    /// WebApplication 扩展方法
    /// </summary>
    public static class WebApplicationExtensions
    {
        /// <summary>
        /// 配置中间件管道
        /// </summary>
        public static WebApplication ConfigureMiddlewarePipeline(this WebApplication app)
        {
            // 1. 确保 CORS 最先处理，包括处理 OPTIONS 预检请求
            app.UseCors("AllowSpecificOrigins");

            // Use Custom Global Exception Middleware
            app.UseMiddleware<ExceptionMiddleware>();

            // 始终启用 Swagger
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "ScadaServer API v1");
                c.RoutePrefix = string.Empty; // 这会让 Swagger 成为首页
            });

            // app.UseHttpsRedirection();

            // 开放 API 动态路由网关分支：所有 /open/* 请求交给 ExposedApiMiddleware。
            // 放在 UseRouting 之前，作为独立的终端分支处理，避免与 /api、/hubs 系统路由冲突。
            app.Map("/open", openApp => openApp.UseMiddleware<ExposedApiMiddleware>());

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
            app.MapHub<ScadaHub>("/hubs/scada");
            // 系统日志推送 Hub（[Authorize]：仅登录客户端可连接，避免匿名泄露运行日志）
            app.MapHub<SystemLogHub>("/hubs/systemlog");

            return app;
        }
    }
}