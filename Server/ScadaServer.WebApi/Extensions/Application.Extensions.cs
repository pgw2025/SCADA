using Microsoft.Extensions.DependencyInjection;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Services;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.WebApi.Services;

namespace ScadaServer.WebApi.Extensions
{
    /// <summary>
    /// 应用服务注册扩展
    /// </summary>
    public static partial class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加应用层服务
        /// </summary>
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<IAlarmRuleAppService, AlarmRuleAppService>();
            services.AddScoped<ILinkageRuleAppService, LinkageRuleAppService>();
            services.AddScoped<IAreaAppService, AreaAppService>();
            services.AddScoped<IConfigLogAppService, ConfigLogAppService>();
            services.AddScoped<IDatabaseConfigAppService, DatabaseConfigAppService>();
            services.AddScoped<IDataConversionAppService, DataConversionAppService>();
            services.AddScoped<IDataModelAppService, DataModelAppService>();
            services.AddScoped<IDeviceAppService, DeviceAppService>();
            services.AddScoped<DatabaseInitializer>();
            services.AddScoped<IExposedInterfaceAppService, ExposedInterfaceAppService>();
            services.AddScoped<IHmiComponentAppService, HmiComponentAppService>();
            services.AddScoped<IModelVariableAppService, ModelVariableAppService>();
            services.AddScoped<IDeviceVariableAppService, DeviceVariableAppService>();
            services.AddScoped<IProtocolAppService, ProtocolAppService>();
            services.AddScoped<IMqttServerAppService, MqttServerAppService>();
            services.AddScoped<IScadaPageAppService, ScadaPageAppService>();
            services.AddScoped<IScadaProjectAppService, ScadaProjectAppService>();
            services.AddScoped<IScheduledTaskAppService, ScheduledTaskAppService>();
            services.AddScoped<ISensorAppService, SensorAppService>();
            services.AddScoped<ISystemConfigAppService, SystemConfigAppService>();
            services.AddScoped<ISystemLogAppService, SystemLogAppService>();
            services.AddScoped<ISystemScriptAppService, SystemScriptAppService>();
            services.AddScoped<ISystemUserAppService, SystemUserAppService>();
            services.AddScoped<IVariableTriggerAppService, VariableTriggerAppService>();

            services.AddSingleton<IMqttManager, MqttManager>();
            services.AddSingleton<IScadaNotificationService, SignalRNotificationService>();

            // 设备状态持久化订阅者（构造时订阅运行时状态变更事件，Singleton 常驻）
            services.AddSingleton<DeviceStatusPersistenceSubscriber>();

            return services;
        }
    }
}