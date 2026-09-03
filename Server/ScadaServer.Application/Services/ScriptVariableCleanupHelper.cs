using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 删除设备变量（DataPointMapping）时的系统脚本联动清理，供设备变量服务与模型变量服务共用：
    /// ① 停用以 "设备键.变量键" 为 OnChange 监听目标的脚本（注明原因，脚本仍承载业务逻辑，需人工确认）；
    /// ② 从 ScopeWrite（"设备.变量" 级）授权中剔除对应条目。
    /// </summary>
    public static class ScriptVariableCleanupHelper
    {
        /// <summary>
        /// 删除设备变量时联动清理相关系统脚本：停用监听该「设备键.变量键」的 OnChange 脚本，
        /// 并从其写授权（ScopeWrite）中剔除对应条目。
        /// </summary>
        public static async Task CleanupScriptsByVariableAsync(
            DataPointMapping entity,
            IDeviceRepository deviceRepository,
            IDataPointRepository dataPointRepository,
            ISystemScriptRepository systemScriptRepository)
        {
            var device = await deviceRepository.GetByIdAsync(entity.DeviceId);
            if (device == null) return;
            var mv = entity.DataPointId > 0 ? await dataPointRepository.GetByIdAsync(entity.DataPointId) : null;
            var deviceKey = device.Key;
            var variableKey = mv?.Key;
            if (string.IsNullOrWhiteSpace(deviceKey) || string.IsNullOrWhiteSpace(variableKey)) return;

            var target = deviceKey + "." + variableKey;

            var scripts = await systemScriptRepository.GetListAsync(s =>
                (s.ScopeWrite != null && s.ScopeWrite.Contains(target))
                || (s.TriggerType == Domain.Enums.ScriptTriggerType.OnChange.ToString()
                    && s.WatchDeviceKey == deviceKey
                    && s.WatchVariableKey == variableKey));

            foreach (var s in scripts)
            {
                bool changed = false;

                if (s.TriggerType == Domain.Enums.ScriptTriggerType.OnChange.ToString()
                    && string.Equals(s.WatchDeviceKey, deviceKey, StringComparison.Ordinal)
                    && string.Equals(s.WatchVariableKey, variableKey, StringComparison.Ordinal))
                {
                    s.Active = false;
                    s.LastError = $"监听变量已被删除，脚本已联动停用（{target}）";
                    changed = true;
                }

                var newWrite = TrimEntries(s.ScopeWrite, e => e == target);
                if (!string.Equals(newWrite, s.ScopeWrite, StringComparison.Ordinal))
                {
                    s.ScopeWrite = newWrite;
                    changed = true;
                }

                if (changed)
                {
                    await systemScriptRepository.UpdateAsync(s);
                }
            }
        }

        /// <summary>
        /// 从分号分隔的授权串中剔除所有满足 <paramref name="match"/> 的条目；无剔除项时保持原串不变。
        /// </summary>
        public static string? TrimEntries(string? raw, Predicate<string> match)
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