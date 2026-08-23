using Microsoft.Extensions.DependencyInjection;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Infrastructure.Configuration;
using ScadaServer.Infrastructure.Services;
using ScadaServer.Runtime;

namespace ScadaServer.WebApi.Extensions
{
    /// <summary>
    /// 基础设施服务注册扩展
    /// </summary>
    public static partial class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加基础设施层服务
        /// </summary>
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
        {
            // 核心基础设施单例服务
            services.AddSingleton<DeviceRegistry>();
            services.AddSingleton<IProtocolDriverFactory, ProtocolDriverFactory>();
            services.AddSingleton<DatabaseConfigManager>();
            services.AddSingleton<SystemMonitorService>();
            services.AddHostedService(sp => sp.GetRequiredService<SystemMonitorService>());

            // Runtime 运行时服务
            services.AddSingleton<RuntimeManager>();
            services.AddHostedService<ScadaServer.WebApi.HostedServices.RuntimeHostedService>();

            return services;
        }
    }
}