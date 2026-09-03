using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 设备删除服务实现：删除设备前检查对外接口引用，并在同一事务内级联清理
    /// 传感器、协议配置、关联系统脚本后再删除设备本身。
    /// <para>注：设备变量（DataPointMapping）随设备删除由数据库外键级联清理（ON DELETE CASCADE），无需在此显式删除。</para>
    /// </summary>
    public class DeviceDeletionService : IDeviceDeletionService
    {
        /// <summary>设备仓储。</summary>
        private readonly IDeviceRepository _repository;
        /// <summary>传感器仓储。</summary>
        private readonly ISensorRepository _sensorRepository;
        /// <summary>对外接口仓储，用于删除前的依赖检查。</summary>
        private readonly IExposedInterfaceRepository _interfaceRepository;
        /// <summary>报警规则仓储。</summary>
        private readonly IAlarmRuleRepository _alarmRuleRepository;
        /// <summary>报警记录仓储。</summary>
        private readonly IAlarmRecordRepository _alarmRecordRepository;
        /// <summary>系统脚本仓储，用于联动清理引用被删设备的脚本。</summary>
        private readonly ISystemScriptRepository _systemScriptRepository;
        /// <summary>设备连接仓储（阶段 3：删除设备时清理其独占连接）。</summary>
        private readonly IDeviceConnectionRepository _connectionRepository;
        /// <summary>控制器仓储（阶段 3：删除设备时清理其独占控制器）。</summary>
        private readonly IControllerRepository _controllerRepository;
        /// <summary>工作单元，提供事务能力。</summary>
        private readonly IUnitOfWork _uow;

        /// <summary>构造函数：注入设备删除所需的相关仓储与事务单元。</summary>
        public DeviceDeletionService(
            IDeviceRepository repository,
            ISensorRepository sensorRepository,
            IExposedInterfaceRepository interfaceRepository,
            IAlarmRuleRepository alarmRuleRepository,
            IAlarmRecordRepository alarmRecordRepository,
            ISystemScriptRepository systemScriptRepository,
            IDeviceConnectionRepository connectionRepository,
            IControllerRepository controllerRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _sensorRepository = sensorRepository;
            _interfaceRepository = interfaceRepository;
            _alarmRuleRepository = alarmRuleRepository;
            _alarmRecordRepository = alarmRecordRepository;
            _systemScriptRepository = systemScriptRepository;
            _connectionRepository = connectionRepository;
            _controllerRepository = controllerRepository;
            _uow = uow;
        }

        /// <summary>按主键删除设备：先检查对外接口引用，再在事务内级联清理数据后删除设备本身。</summary>
        public async Task DeleteAsync(int deviceId)
        {
            var entity = await _repository.GetByIdAsync(deviceId);
            if (entity == null) return;

            // 1. 依赖检查：检查是否被对外接口引用
            var interfaces = await _interfaceRepository.GetListAsync(i => i.DeviceId == deviceId);
            if (interfaces.Any())
            {
                throw new BusinessException($"无法删除设备 '{entity.Name}'，因为它已被配置到 {interfaces.Count} 个对外数据接口中。请先解除绑定。");
            }

            await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                // 删除级联数据（协议配置已内联于 Device 行，随设备删除一并清除）
                await _sensorRepository.DeleteRangeAsync(s => s.DeviceId == deviceId);
                await _alarmRuleRepository.DeleteRangeAsync(ar => ar.DeviceId == deviceId);

                // 报警 recover 兜底：设备将被删除，其未恢复报警不可能再收到真实恢复事件，
                // 一次性批量标记为已恢复，避免遗留幽灵未恢复告警。
                await _alarmRecordRepository.RecoverByDeviceAsync(deviceId, DateTime.UtcNow);

                // 联动：系统脚本引用清理。设备删除后其变量随之消失，
                // 脚本引擎不会再收到该设备的变量变化事件；为保持数据一致，
                // 一并停用「监听该设备」的 OnChange 脚本，并从读/写授权 scope 中剔除该设备条目。
                await CleanupScriptsByDeviceAsync(entity.Key);

                // 删除设备（先释放 Device → Controller/Connection 的 Restrict 外键引用）
                await _repository.DeleteAsync(entity);

                // 阶段 3 级联清理：删除设备独占的 DeviceConnection + Controller（P3-A/P3-D 过渡列指向）。
                // 仅当无其他设备/连接仍引用时才删除，避免误删手工共享（多设备共用连接）的资源。
                await CleanupExclusiveControllerAndConnectionAsync(entity);

                return true;
            });
        }

        /// <summary>
        /// 级联清理设备独占的 <see cref="DeviceConnection"/> 与 <see cref="Controller"/>（阶段 3）。
        /// <para>
        /// 过渡期结构为"1 设备 = 1 独占 Connection + 1 独占 Controller"；但允许手工演进为多设备共享，
        /// 故删除前检查引用：Connection 仅当无其他 Device.ConnectionId 指向时删除；
        /// Controller 仅当无其他 Device.ControllerId 指向且无其他 Connection.ControllerId 指向时删除。
        /// </para>
        /// </summary>
        private async Task CleanupExclusiveControllerAndConnectionAsync(Device entity)
        {
            // Connection：设备行已删除，若仍有其它设备指向则保留（共享连接场景）。
            // 用 DeleteRangeAsync（跟踪查询）删除，避免 AsNoTracking 图 Remove 引发的重复跟踪冲突。
            if (entity.ConnectionId.HasValue)
            {
                var usedByOtherDevices = await _repository.AnyAsync(d => d.ConnectionId == entity.ConnectionId.Value);
                if (!usedByOtherDevices)
                {
                    await _connectionRepository.DeleteRangeAsync(c => c.Id == entity.ConnectionId.Value);
                }
            }

            // Controller：无其它设备指向且控制器下无其它连接时才删除。
            if (entity.ControllerId.HasValue)
            {
                var usedByOtherDevices = await _repository.AnyAsync(d => d.ControllerId == entity.ControllerId.Value);
                var hasOtherConnections = await _connectionRepository.AnyAsync(c => c.ControllerId == entity.ControllerId.Value);
                if (!usedByOtherDevices && !hasOtherConnections)
                {
                    await _controllerRepository.DeleteRangeAsync(c => c.Id == entity.ControllerId.Value);
                }
            }
        }

        /// <summary>
        /// 联动清理引用被删设备的系统脚本：
        /// ① 停用以该设备为 OnChange 监听目标的脚本（注明原因）；
        /// ② 从 ScopeRead（设备级）/ ScopeWrite（"设备.变量" 级）授权中剔除该设备的条目。
        /// </summary>
        private async Task CleanupScriptsByDeviceAsync(string deviceKey)
        {
            var scripts = await _systemScriptRepository.GetListAsync(s =>
                (s.WatchDeviceKey != null && s.WatchDeviceKey == deviceKey)
                || (s.ScopeRead != null && s.ScopeRead.Contains(deviceKey))
                || (s.ScopeWrite != null && s.ScopeWrite.Contains(deviceKey)));

            var devPrefix = deviceKey + ".";
            foreach (var s in scripts)
            {
                bool changed = false;

                // ① 停用监听该设备的 OnChange 脚本
                if (s.TriggerType == ScriptTriggerType.OnChange.ToString()
                    && string.Equals(s.WatchDeviceKey, deviceKey, StringComparison.Ordinal))
                {
                    s.Active = false;
                    s.LastError = $"监听设备被删除，脚本已联动停用（设备 {deviceKey}）";
                    changed = true;
                }

                // ② 从授权 scope 中剔除对已删除设备的引用（精确匹配分号分隔项）
                var newRead = TrimEntries(s.ScopeRead, e => e == deviceKey);
                if (!string.Equals(newRead, s.ScopeRead, StringComparison.Ordinal)) { s.ScopeRead = newRead; changed = true; }

                var newWrite = TrimEntries(s.ScopeWrite, e => e == deviceKey || e.StartsWith(devPrefix, StringComparison.Ordinal));
                if (!string.Equals(newWrite, s.ScopeWrite, StringComparison.Ordinal)) { s.ScopeWrite = newWrite; changed = true; }

                if (changed)
                {
                    await _systemScriptRepository.UpdateAsync(s);
                }
            }
        }

        /// <summary>
        /// 从分号分隔的授权串中剔除所有满足 <paramref name="match"/> 的条目；无剔除项时保持原串不变。
        /// </summary>
        private static string? TrimEntries(string? raw, Predicate<string> match)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            var entries = raw.Split(';').Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
            var removed = entries.Where(e => match(e)).ToList();
            if (removed.Count == 0) return raw;

            var kept = entries.Where(e => !match(e)).ToList();
            return kept.Count == 0 ? null : string.Join(';', kept);
        }
    }
}