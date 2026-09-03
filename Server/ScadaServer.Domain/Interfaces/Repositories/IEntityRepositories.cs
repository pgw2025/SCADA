using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Interfaces.Repositories
{
    /// <summary>
    /// 报警规则仓储接口
    /// </summary>
    public interface IAlarmRuleRepository : IRepository<AlarmRule, int> { }

    /// <summary>
    /// 联动规则仓储接口
    /// </summary>
    public interface ILinkageRuleRepository : IRepository<LinkageRule, int> { }

    /// <summary>
    /// 区域仓储接口
    /// </summary>
    public interface IAreaRepository : IRepository<Area, int> { }

    /// <summary>
    /// 配置日志仓储接口
    /// </summary>
    public interface IConfigLogRepository : IRepository<ConfigLog, int> { }

    /// <summary>
    /// 控制器仓储接口（控制器/PLC 资产台账，阶段 2）。
    /// </summary>
    public interface IControllerRepository : IRepository<Controller, int>
    {
        /// <summary>
        /// 按条件分页查询控制器（协议/关键字过滤 + 分页），返回总数与当前页数据。
        /// </summary>
        Task<(int Total, List<Controller> Items)> QueryAsync(
            int? protocolId,
            string? keyword,
            int pageIndex,
            int pageSize);

        /// <summary>
        /// 更新专用加载：跟踪查询，仅加载控制器本体（不含导航），使 Update(entity) 无需附加导航对象图。
        /// </summary>
        Task<Controller?> GetByIdForUpdateAsync(int id);
    }

    /// <summary>
    /// 数据库配置仓储接口
    /// </summary>
    public interface IDatabaseConfigRepository : IRepository<DatabaseConfig, int> { }

    /// <summary>
    /// 数据转换仓储接口
    /// </summary>
    public interface IDataConversionRepository : IRepository<DataConversion, int> { }

    /// <summary>
    /// 设备连接仓储接口（阶段 3：连接参数抽取实体）。
    /// </summary>
    public interface IDeviceConnectionRepository : IRepository<DeviceConnection, int>
    {
        /// <summary>
        /// 更新专用加载：跟踪查询，仅加载连接本体（不含导航），使 Update(entity) 无需附加导航对象图。
        /// </summary>
        Task<DeviceConnection?> GetByIdForUpdateAsync(int id);

        /// <summary>
        /// 统计每个控制器下的连接数（ControllerId → 数量），供控制器列表/详情展示连接数量。
        /// </summary>
        Task<Dictionary<int, int>> GetCountsByControllerAsync();
    }

    /// <summary>
    /// 设备仓储接口
    /// </summary>
    public interface IDeviceRepository : IRepository<Device, int>
    {
        /// <summary>
        /// 按主键加载设备及其协议配置（跟踪查询），专供更新场景使用。
        /// 仅 Include Config（更新事务中唯一需要的导航），避免附加大对象图引发实体跟踪冲突。
        /// </summary>
        Task<Device?> GetByIdForUpdateAsync(int id);

        /// <summary>
        /// 按区域统计设备数量（AreaId → 数量），供区域树展示各节点直接挂载的设备数。
        /// </summary>
        Task<Dictionary<int, int>> GetCountByAreaAsync();
    }

    /// <summary>
    /// 设备-数据模型绑定仓储接口（阶段 5：DeviceDataModels 多对多中间表）。
    /// <para>
    /// 承载「设备 ↔ 数据模型」绑定行的查询；增删改走基类通用 CRUD。
    /// 主模型（IsPrimary=true）行与 Device.ModelId 的双写一致性收敛于应用服务事务单点。
    /// </para>
    /// </summary>
    public interface IDeviceDataModelRepository : IRepository<DeviceDataModel, int>
    {
        /// <summary>
        /// 查询某设备的全部绑定（含 DataModel 摘要导航，AsNoTracking）。
        /// 返回顺序：主模型优先，其次按 Id 升序（展示/管理稳定序）。
        /// </summary>
        Task<List<DeviceDataModel>> GetByDeviceAsync(int deviceId);

        /// <summary>
        /// 批量查询多台设备的绑定（含 DataModel 摘要导航，AsNoTracking），
        /// 供设备列表 N+1 优化——一次性加载全部设备的绑定行后按 DeviceId 分组。
        /// </summary>
        Task<List<DeviceDataModel>> GetByDevicesAsync(IEnumerable<int> deviceIds);

        /// <summary>
        /// 更新专用加载（跟踪查询，不含导航）：加载某设备全部绑定行用于主模型降级/切换，
        /// 使 Update(entity) 无需附加导航对象图，避免跟踪冲突。
        /// </summary>
        Task<List<DeviceDataModel>> GetByDeviceForUpdateAsync(int deviceId);
    }

    /// <summary>
    /// 数据模型仓储接口。
    /// <para>
    /// 读路径（<see cref="IRepository{TEntity,TKey}.GetByIdAsync(TKey)"/>）由实现覆盖为
    /// AsNoTracking + Include Protocol；更新/删除路径须使用 <see cref="GetByIdForUpdateAsync"/>
    /// （跟踪查询、不含导航），避免 Update/Remove 附加导航对象图与协议校验加载的 Protocol
    /// 产生同主键跟踪冲突（与 Controller/Device/DeviceConnection 仓储一致）。
    /// </para>
    /// </summary>
    public interface IDataModelRepository : IRepository<DataModel, int>
    {
        /// <summary>
        /// 更新/删除专用加载（跟踪查询、不含导航）：仅加载模型本体。
        /// 不使用 AsNoTracking、不 Include Protocol，使 Update(entity) / Remove(entity)
        /// 无需附加导航对象图，避免与协议存在性校验加载的 Protocol 实体产生同主键跟踪冲突。
        /// </summary>
        Task<DataModel?> GetByIdForUpdateAsync(int id);
    }

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
    public interface IDataPointRepository : IRepository<DataPoint, int> { }

    /// <summary>
    /// 通信协议仓储接口
    /// </summary>
    public interface IProtocolRepository : IRepository<Protocol, int> { }

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
    public interface ISystemLogRepository : IRepository<SystemLog, int>
    {
        /// <summary>
        /// 按查询条件分页查询系统日志（分类/级别/关键字/时间段 + 分页），返回总数与当前页数据。
        /// </summary>
        Task<(int Total, List<SystemLog> Items)> QueryAsync(
            string? category,
            List<string>? levels,
            string? keyword,
            string? source,
            DateTime? startTime,
            DateTime? endTime,
            int pageIndex,
            int pageSize);

        /// <summary>
        /// 按分类/时间段批量清理日志，返回删除条数。
        /// </summary>
        Task<int> ClearAsync(string? category, DateTime? startTime, DateTime? endTime);
    }

    /// <summary>
    /// 系统脚本仓储接口
    /// </summary>
    public interface ISystemScriptRepository : IRepository<SystemScript, int> { }

    /// <summary>
    /// 脚本执行记录仓储接口（主键为 long，数据量大）。
    /// </summary>
    public interface IScriptExecutionRecordRepository : IRepository<ScriptExecutionRecord, long>
    {
        /// <summary>
        /// 按脚本分页查询执行记录（结果筛选可选），返回总数与当前页数据。
        /// </summary>
        Task<(int Total, List<ScriptExecutionRecord> Items)> QueryByScriptAsync(
            int scriptId,
            string? result,
            int pageIndex,
            int pageSize);

        /// <summary>
        /// 删除指定时间之前的执行记录（按 StartedAt），返回删除条数；供清理服务分批调用。
        /// </summary>
        Task<int> DeleteOlderThanAsync(DateTime cutoff, int batchSize);
    }

    /// <summary>
    /// 系统用户仓储接口
    /// </summary>
    public interface ISystemUserRepository : IRepository<SystemUser, int> { }

    /// <summary>
    /// 变量历史数据仓储接口（主键为 long，历史数据量大）
    /// </summary>
    public interface IVariableHistoryRepository : IRepository<VariableHistory, long>
    {
        /// <summary>
        /// 查询指定设备下某变量的最近 limit 条记录（按采样时间倒序，SQL 下推取数，避免全表回拉）。
        /// <para>deviceKey 可为空，空时按全设备查询（兼容无设备上下文的历史查询）。</para>
        /// </summary>
        Task<List<VariableHistory>> GetLatestAsync(
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start = null,
            DateTime? end = null);

        /// <summary>
        /// 按主键升序分页拉取历史原始数据（历史迁移用，AsNoTracking 大页读取）。
        /// <paramref name="afterId"/> 为“跳过 ≤ 该 Id 的行”；连续调用传上一批末条 Id 即可全表游标遍历。
        /// </summary>
        Task<List<VariableHistory>> GetBatchAfterIdAsync(long afterId, int size);
    }

    /// <summary>
    /// 变量实时快照仓储接口（复合主键 DeviceId+VariableKey）。
    /// 用于从 MySQL 实时库读取各设备各变量的最新快照。
    /// </summary>
    public interface IVariableRealtimeRepository
    {
        /// <summary>
        /// 查询指定设备下某变量的最新实时快照；未找到返回 null。
        /// </summary>
        Task<VariableRealtime?> GetByDeviceAndKeyAsync(int deviceId, string variableKey);

        /// <summary>
        /// 查询指定设备（deviceKey 匹配）下所有变量的最新实时快照。
        /// </summary>
        Task<List<VariableRealtime>> GetAllByDeviceAsync(string deviceKey);
    }

    /// <summary>
    /// 报警记录仓储接口（主键为 long，数据量大）。
    /// </summary>
    public interface IAlarmRecordRepository : IRepository<AlarmRecord, long>
    {
        /// <summary>
        /// 按复合条件分页查询报警记录（设备/级别/未确认/未恢复/时间段），返回总数与当前页数据。
        /// </summary>
        Task<(int Total, List<AlarmRecord> Items)> QueryAsync(
            int? deviceId,
            AlarmLevelEnum? level,
            bool? unacked,
            bool? unrecovered,
            DateTime? startTime,
            DateTime? endTime,
            int pageIndex,
            int pageSize);

        /// <summary>
        /// 查询最近一条"同键未恢复"的记录ID（用于恢复事件关联）。找不到返回 null。
        /// </summary>
        Task<long?> FindUnrecoveredIdAsync(int deviceId, string variableKey, long? ruleId);

        /// <summary>
        /// 恢复指定设备上所有未恢复的报警记录（设备删除联动兜底），返回恢复条数。
        /// </summary>
        Task<int> RecoverByDeviceAsync(int deviceId, DateTime recoverAt);
    }
}
