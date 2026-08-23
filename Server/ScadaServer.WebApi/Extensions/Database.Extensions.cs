using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Infrastructure.Repositories;
using SqlSugar;

namespace ScadaServer.WebApi.Extensions
{
    /// <summary>
    /// 数据库相关服务注册扩展
    /// </summary>
    public static partial class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加数据库服务（SqlSugar + UnitOfWork + Repositories）
        /// </summary>
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services)
        {
            // 1. Register SqlSugar (Scoped)
            services.AddScoped<ISqlSugarClient>(s =>
            {
                var options = s.GetRequiredService<IOptions<SystemDbOptions>>().Value;
                return new SqlSugarScope(new ConnectionConfig()
                {
                    ConnectionString = options.GetConnectionString(),
                    DbType = DbType.MySql,
                    IsAutoCloseConnection = true
                });
            });

            // 2. Register Unit of Work
            services.AddScoped<IUnitOfWork, SqlSugarUnitOfWork>();

            // 3. Register Repositories
            services.AddScoped<IAlarmRuleRepository, AlarmRuleRepository>();
            services.AddScoped<IAreaRepository, AreaRepository>();
            services.AddScoped<IConfigLogRepository, ConfigLogRepository>();
            services.AddScoped<IDatabaseConfigRepository, DatabaseConfigRepository>();
            services.AddScoped<IDataConversionRepository, DataConversionRepository>();
            services.AddScoped<IDataModelRepository, DataModelRepository>();
            services.AddScoped<IDeviceRepository, DeviceRepository>();
            services.AddScoped<IRepository<DeviceConfig, int>, DeviceConfigRepository>();
            services.AddScoped<IExposedInterfaceRepository, ExposedInterfaceRepository>();
            services.AddScoped<IHmiComponentRepository, HmiComponentRepository>();
            services.AddScoped<IModelVariableRepository, ModelVariableRepository>();
            services.AddScoped<IMqttServerRepository, MqttServerRepository>();
            services.AddScoped<IRepository<MqttVariableConfig, int>, MqttVariableConfigRepository>();
            services.AddScoped<IScadaPageRepository, ScadaPageRepository>();
            services.AddScoped<IScadaProjectRepository, ScadaProjectRepository>();
            services.AddScoped<IScheduledTaskRepository, ScheduledTaskRepository>();
            services.AddScoped<ISensorRepository, SensorRepository>();
            services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
            services.AddScoped<ISystemLogRepository, SystemLogRepository>();
            services.AddScoped<ISystemScriptRepository, SystemScriptRepository>();
            services.AddScoped<ISystemUserRepository, SystemUserRepository>();
            services.AddScoped<IVariableTriggerRepository, VariableTriggerRepository>();

            return services;
        }
    }
}