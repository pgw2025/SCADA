using ScadaServer.Domain.Entities;

namespace ScadaServer.Domain.Interfaces.Repositories
{
    /// <summary>
    /// 报警规则仓储接口
    /// </summary>
    public interface IAlarmRuleRepository : IRepository<AlarmRule, int> { }

    /// <summary>
    /// 区域仓储接口
    /// </summary>
    public interface IAreaRepository : IRepository<Area, int> { }

    /// <summary>
    /// 配置日志仓储接口
    /// </summary>
    public interface IConfigLogRepository : IRepository<ConfigLog, int> { }

    /// <summary>
    /// 数据库配置仓储接口
    /// </summary>
    public interface IDatabaseConfigRepository : IRepository<DatabaseConfig, int> { }

    /// <summary>
    /// 数据转换仓储接口
    /// </summary>
    public interface IDataConversionRepository : IRepository<DataConversion, int> { }

    /// <summary>
    /// 设备仓储接口
    /// </summary>
    public interface IDeviceRepository : IRepository<Device, int> { }

    /// <summary>
    /// 数据模型仓储接口
    /// </summary>
    public interface IDataModelRepository : IRepository<DataModel, int> { }

    /// <summary>
    /// 暴露接口仓储接口
    /// </summary>
    public interface IExposedInterfaceRepository : IRepository<ExposedInterface, int> { }

    /// <summary>
    /// HMI组件仓储接口
    /// </summary>
    public interface IHmiComponentRepository : IRepository<HmiComponent, int> { }

    /// <summary>
    /// 模型变量仓储接口
    /// </summary>
    public interface IModelVariableRepository : IRepository<ModelVariable, int> { }

    /// <summary>
    /// MQTT服务器仓储接口
    /// </summary>
    public interface IMqttServerRepository : IRepository<MqttServer, int> { }

    /// <summary>
    /// SCADA页面仓储接口
    /// </summary>
    public interface IScadaPageRepository : IRepository<ScadaPage, int> { }

    /// <summary>
    /// SCADA项目仓储接口
    /// </summary>
    public interface IScadaProjectRepository : IRepository<ScadaProject, int> { }

    /// <summary>
    /// 定时任务仓储接口
    /// </summary>
    public interface IScheduledTaskRepository : IRepository<ScheduledTask, int> { }

    /// <summary>
    /// 传感器仓储接口
    /// </summary>
    public interface ISensorRepository : IRepository<Sensor, int> { }

    /// <summary>
    /// 系统配置仓储接口
    /// </summary>
    public interface ISystemConfigRepository : IRepository<SystemConfig, int> { }

    /// <summary>
    /// 系统日志仓储接口
    /// </summary>
    public interface ISystemLogRepository : IRepository<SystemLog, int> { }

    /// <summary>
    /// 系统脚本仓储接口
    /// </summary>
    public interface ISystemScriptRepository : IRepository<SystemScript, int> { }

    /// <summary>
    /// 系统用户仓储接口
    /// </summary>
    public interface ISystemUserRepository : IRepository<SystemUser, int> { }

    /// <summary>
    /// 变量触发器仓储接口
    /// </summary>
    public interface IVariableTriggerRepository : IRepository<VariableTrigger, int> { }
}
