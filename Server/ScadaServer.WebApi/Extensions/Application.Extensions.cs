using Microsoft.Extensions.DependencyInjection;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.ImportExport;
using ScadaServer.Application.Services;
using ScadaServer.Infrastructure.Communication;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Infrastructure.Services;
using ScadaServer.WebApi.HostedServices;
using ScadaServer.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.Options;
using ScadaServer.WebApi.Hubs;

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
            services.AddScoped<IControllerAppService, ControllerAppService>();
            services.AddScoped<IDatabaseConfigAppService, DatabaseConfigAppService>();
            // 消息通知（钉钉/SMTP）配置管理：读写 override 文件 + 临时值测试发送。
            services.AddScoped<INotificationConfigService, NotificationConfigService>();
            services.AddScoped<IDataConversionAppService, DataConversionAppService>();
            services.AddScoped<IDataModelAppService, DataModelAppService>();
            services.AddScoped<IDeviceAppService, DeviceAppService>();
            services.AddScoped<IDeviceConnectionAppService, DeviceConnectionAppService>();
            services.AddScoped<IDeviceDataModelAppService, DeviceDataModelAppService>();
            services.AddScoped<IDeviceDeletionService, DeviceDeletionService>();
            services.AddScoped<DatabaseInitializer>();
            services.AddScoped<IExposedInterfaceAppService, ExposedInterfaceAppService>();
            // 开放 API 暴露接口配置注册表：单例常驻，内存缓存 /open/* 网关匹配的启用接口。
            services.AddSingleton<IExposedApiRegistry, ExposedApiRegistry>();
            services.AddScoped<IHmiComponentAppService, HmiComponentAppService>();
            // 组态图片图库：文件目录存储，无数据库依赖。经工厂注入 ContentRootPath 解析存储根目录
            // （Application 层不引用 ASP.NET Core 类型，运行期由 sp 解析 IWebHostEnvironment）。
            services.AddScoped<IHmiImageAppService>(sp => new HmiImageAppService(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ScadaServer.Application.Options.HmiImageOptions>>().Value,
                sp.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath));
            services.AddScoped<IDataPointAppService, DataPointAppService>();

            // 模型变量导入/导出：解析器与导出服务均无状态，可注册为常驻单例。
            services.AddSingleton<IVariableImportParser, VariableImportParser>();
            services.AddSingleton<VariableExportService>();
            services.AddScoped<IDataPointMappingAppService, DataPointMappingAppService>();
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
            // ========== 外部消息通知（钉钉机器人 / SMTP 邮件）==========
            // 命名 HttpClient：钉钉 webhook 8s 超时（替代默认 100s，避免拖死后台发送循环）。
            services.AddHttpClient(DingTalkRobotClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(8));
            services.AddSingleton<IExternalMessageSender, DingTalkRobotClient>();
            services.AddSingleton<IExternalMessageSender, EmailSender>();

            // 外部消息后台推送服务：主队列扇出 -> 各渠道独立通道（互不阻塞）+ 限流 + 重试。
            services.AddSingleton<ExternalNotificationService>();
            services.AddSingleton<IExternalNotificationQueue>(sp => sp.GetRequiredService<ExternalNotificationService>());
            services.AddHostedService(sp => sp.GetRequiredService<ExternalNotificationService>());

            // 原 SignalR 通知实现注册为具体类型，由装饰器包裹后以接口暴露：
            // 前端 SignalR/MQTT 推送路径不变，可外发事件额外进入钉钉/邮件队列。
            services.AddSingleton<SignalRNotificationService>();
            services.AddSingleton<IScadaNotificationService>(sp => new ExternalNotificationDecorator(
                sp.GetRequiredService<SignalRNotificationService>(),
                sp.GetRequiredService<IExternalNotificationQueue>(),
                sp.GetRequiredService<IOptions<NotificationOptions>>(),
                sp.GetRequiredService<ILogger<ExternalNotificationDecorator>>()));

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