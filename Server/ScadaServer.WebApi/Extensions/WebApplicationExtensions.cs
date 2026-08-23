using Microsoft.AspNetCore.Builder;
using ScadaServer.Application.Interfaces;
using ScadaServer.Infrastructure.Persistence;
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

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
            app.MapHub<ScadaHub>("/hubs/scada");

            return app;
        }

        /// <summary>
        /// 执行启动初始化逻辑
        /// </summary>
        public static async Task InitializeAsync(this WebApplication app)
        {
            // 自动初始化数据库表结构
            using (var scope = app.Services.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
                await initializer.InitializeAsync();
            }

            // 初始化 MQTT 管理器
            var mqttManager = app.Services.GetRequiredService<IMqttManager>();
            await mqttManager.StartAsync();
        }
    }
}