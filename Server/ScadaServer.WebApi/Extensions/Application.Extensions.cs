using Microsoft.Extensions.DependencyInjection;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Services;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.WebApi.HostedServices;
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
            services.AddScoped<IAlarmRecordAppService, AlarmRecordAppService>();
            services.AddScoped<ILinkageRuleAppService, LinkageRuleAppService>();
            services.AddScoped<IAreaAppService, AreaAppService>();
            services.AddScoped<IConfigLogAppService, ConfigLogAppService>();
            services.AddScoped<IDatabaseConfigAppService, DatabaseConfigAppService>();
            services.AddScoped<IDataConversionAppService, DataConversionAppService>();
            services.AddScoped<IDataModelAppService, DataModelAppService>();
            services.AddScoped<IDeviceAppService, DeviceAppService>();
            services.AddScoped<IDeviceDeletionService, DeviceDeletionService>();
            services.AddScoped<DatabaseInitializer>();
            services.AddScoped<IExposedInterfaceAppService, ExposedInterfaceAppService>();
            // 开放 API 暴露接口配置注册表：单例常驻，内存缓存 /open/* 网关匹配的启用接口。
            services.AddSingleton<IExposedApiRegistry, ExposedApiRegistry>();
            services.AddScoped<IHmiComponentAppService, HmiComponentAppService>();
            services.AddScoped<IModelVariableAppService, ModelVariableAppService>();
            services.AddScoped<IDeviceVariableAppService, DeviceVariableAppService>();
            services.AddScoped<IProtocolAppService, ProtocolAppService>();
            services.AddScoped<IMqttServerAppService, MqttServerAppService>();
            services.AddScoped<IMqttVariableConfigAppService, MqttVariableConfigAppService>();
            services.AddScoped<IScadaPageAppService, ScadaPageAppService>();
            services.AddScoped<IScadaProjectAppService, ScadaProjectAppService>();
            services.AddScoped<IScheduledTaskAppService, ScheduledTaskAppService>();
            services.AddScoped<ISensorAppService, SensorAppService>();
            services.AddScoped<ISystemConfigAppService, SystemConfigAppService>();
            services.AddScoped<ISystemLogAppService, SystemLogAppService>();
            services.AddScoped<ISystemScriptAppService, SystemScriptAppService>();
            services.AddScoped<IScriptValidationService, ScriptValidationService>();
            services.AddScoped<ISystemUserAppService, SystemUserAppService>();
            services.AddScoped<IHistoryAppService, HistoryAppService>();

            // 历史数据记录器：采集线程异步入队，后台批量落库（单例 + IHostedService 常驻）。
            services.AddSingleton<HistoryRecorder>();
            services.AddSingleton<IHistoryRecorder>(sp => sp.GetRequiredService<HistoryRecorder>());
            services.AddHostedService(sp => sp.GetRequiredService<HistoryRecorder>());

            // 报警记录器：运行时报警事件异步入队，后台批量落库（单例 + IHostedService 常驻）。
            services.AddSingleton<AlarmRecorder>();
            services.AddSingleton<IAlarmRecorder>(sp => sp.GetRequiredService<AlarmRecorder>());
            services.AddHostedService(sp => sp.GetRequiredService<AlarmRecorder>());

            // 系统日志记录器：运行日志（有界可丢）+ 操作/安全日志（无界不丢）统一批量落库 + SignalR 广播。
            services.AddSingleton<SystemLogRecorder>();
            services.AddHostedService(sp => sp.GetRequiredService<SystemLogRecorder>());

            // 实时快照服务：采集循环更新内存快照，后台周期性 Upsert 到 VariableRealtime（MySQL 实时库）。
            services.AddSingleton<RealtimeSnapshotService>();
            services.AddSingleton<IRealtimeSnapshotService>(sp => sp.GetRequiredService<RealtimeSnapshotService>());
            services.AddHostedService(sp => sp.GetRequiredService<RealtimeSnapshotService>());

            services.AddSingleton<IMqttManager, MqttManager>();
            services.AddHostedService<MqttReconnectHostedService>();
            services.AddSingleton<IScadaNotificationService, SignalRNotificationService>();

            // 操作日志审计服务（注入 SystemLogRecorder + HttpContext，按请求解析）
            services.AddScoped<IOperationAuditService, OperationAuditService>();

            // 变量写入审计记录器：运行时层（脚本/绑定联动）非 HTTP 写入路径的操作日志桥接（Singleton，与 RuntimeManager 生命周期一致）
            services.AddSingleton<IVariableWriteAuditRecorder, VariableWriteAuditRecorder>();

            // 设备状态持久化订阅者（构造时订阅运行时状态变更事件，Singleton 常驻）
            services.AddSingleton<DeviceStatusPersistenceSubscriber>();

            return services;
        }
    }
}