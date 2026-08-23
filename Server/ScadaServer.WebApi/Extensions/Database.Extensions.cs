using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Infrastructure.Repositories;

namespace ScadaServer.WebApi.Extensions
{
    /// <summary>
    /// 数据库相关服务注册扩展
    /// </summary>
    public static partial class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加数据库服务（EF Core + UnitOfWork + Repositories）
        /// </summary>
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services)
        {
            // 1. Register EF Core DbContext (Scoped)
            services.AddDbContext<ScadaDbContext>((serviceProvider, options) =>
            {
                var dbOptions = serviceProvider
                    .GetRequiredService<IOptions<SystemDbOptions>>().Value;

                var connectionString = dbOptions.GetConnectionString();
                options.UseMySql(
                    connectionString,
                    ServerVersion.AutoDetect(connectionString),
                    mySqlOptions =>
                    {
                        mySqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 3,
                            maxRetryDelay: TimeSpan.FromSeconds(5),
                            errorNumbersToAdd: null);
                    });
            });

            // 2. Register Unit of Work
            services.AddScoped<IUnitOfWork, EfUnitOfWork>();

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
