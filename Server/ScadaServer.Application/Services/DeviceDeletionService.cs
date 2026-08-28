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
    /// <para>注：设备变量（DeviceVariable）随设备删除由数据库外键级联清理（ON DELETE CASCADE），无需在此显式删除。</para>
    /// </summary>
    public class DeviceDeletionService : IDeviceDeletionService
    {
        private readonly IDeviceRepository _repository;
        private readonly ISensorRepository _sensorRepository;
        private readonly IExposedInterfaceRepository _interfaceRepository;
        private readonly IRepository<DeviceConfig, int> _configRepository;
        private readonly IAlarmRuleRepository _alarmRuleRepository;
        private readonly IAlarmRecordRepository _alarmRecordRepository;
        private readonly ISystemScriptRepository _systemScriptRepository;
        private readonly IUnitOfWork _uow;

        public DeviceDeletionService(
            IDeviceRepository repository,
            ISensorRepository sensorRepository,
            IExposedInterfaceRepository interfaceRepository,
            IRepository<DeviceConfig, int> configRepository,
            IAlarmRuleRepository alarmRuleRepository,
            IAlarmRecordRepository alarmRecordRepository,
            ISystemScriptRepository systemScriptRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _sensorRepository = sensorRepository;
            _interfaceRepository = interfaceRepository;
            _configRepository = configRepository;
            _alarmRuleRepository = alarmRuleRepository;
            _alarmRecordRepository = alarmRecordRepository;
            _systemScriptRepository = systemScriptRepository;
            _uow = uow;
        }

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
                // 删除级联数据
                await _sensorRepository.DeleteRangeAsync(s => s.DeviceId == deviceId);
                await _alarmRuleRepository.DeleteRangeAsync(ar => ar.DeviceId == deviceId);

                await _configRepository.DeleteRangeAsync(c => c.DeviceId == deviceId);

                // 报警 recover 兜底：设备将被删除，其未恢复报警不可能再收到真实恢复事件，
                // 一次性批量标记为已恢复，避免遗留幽灵未恢复告警。
                await _alarmRecordRepository.RecoverByDeviceAsync(deviceId, DateTime.UtcNow);

                // 联动：系统脚本引用清理。设备删除后其变量随之消失，
                // 脚本引擎不会再收到该设备的变量变化事件；为保持数据一致，
                // 一并停用「监听该设备」的 OnChange 脚本，并从读/写授权 scope 中剔除该设备条目。
                await CleanupScriptsByDeviceAsync(entity.Key);

                // 删除设备
                await _repository.DeleteAsync(entity);

                return true;
            });
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