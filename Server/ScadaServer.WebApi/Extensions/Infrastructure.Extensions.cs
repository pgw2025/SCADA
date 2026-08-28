using Microsoft.Extensions.DependencyInjection;
using ScadaServer.Application.Interfaces;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Infrastructure.Services;
using ScadaServer.Runtime;
using ScadaServer.Runtime.Interface;
using ScadaServer.Runtime.Events;
using ScadaServer.Runtime.Bindings;
using ScadaServer.Runtime.Alarms;
using ScadaServer.WebApi.Services;

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
            services.AddSingleton<SystemMonitorService>();
            services.AddHostedService(sp => sp.GetRequiredService<SystemMonitorService>());

            // InfluxDB 时序历史库（单例：配置变更通过 Rebuild 热切换客户端）
            services.AddSingleton<ScadaServer.Infrastructure.Influx.InfluxStore>();
            services.AddSingleton<ScadaServer.Application.Interfaces.IInfluxStore>(
                sp => sp.GetRequiredService<ScadaServer.Infrastructure.Influx.InfluxStore>());

            // 运行时数据库管理服务（主库配置 override 文件 + 连接测试）
            services.AddSingleton<IRuntimeDatabaseService, RuntimeDatabaseService>();

            // 历史数据迁移服务（MySQL 存量 → InfluxDB，手动触发）
            services.AddSingleton<IHistoryMigrationService, HistoryMigrationService>();

            // Runtime 运行时服务
            services.AddSingleton<RuntimeManager>();
            services.AddSingleton<IRuntimeManager>(sp => sp.GetRequiredService<RuntimeManager>());
            services.AddSingleton<IRuntimeDeviceManager>(sp => sp.GetRequiredService<RuntimeManager>());
            services.AddSingleton<IRuntimeStatusProvider, RuntimeStatusProviderAdapter>();
            services.AddSingleton<IVariableChangeBus, VariableChangeBus>();
            services.AddSingleton<IVariableBindingEngine, VariableBindingEngine>();
            services.AddSingleton<IAlarmRuleEngine, AlarmRuleEngine>();
            services.AddHostedService<ScadaServer.WebApi.HostedServices.RuntimeHostedService>();

            // MQTT 服务（MqttHandler 当前为占位实现）
            services.AddSingleton<IMqttService, MqttHandler>();

            return services;
        }
    }
}