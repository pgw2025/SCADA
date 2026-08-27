using System.Collections.Generic;
using ScadaServer.Domain;

namespace ScadaServer.Runtime.Alarms
{
    /// <summary>
    /// 一次报警规则求值命中（某条规则在某变量当前值下满足条件）。
    /// </summary>
    public class AlarmHit
    {
        /// <summary>命中的规则ID</summary>
        public long RuleId { get; set; }

        /// <summary>规则名称</summary>
        public string RuleName { get; set; } = string.Empty;

        /// <summary>报警级别</summary>
        public Domain.Enums.AlarmLevelEnum Level { get; set; }

        /// <summary>条件</summary>
        public Domain.Enums.TriggerConditionEnum Condition { get; set; }

        /// <summary>阈值</summary>
        public double Threshold { get; set; }

        /// <summary>实际值（字符串形式）</summary>
        public string? ActualValue { get; set; }

        /// <summary>是否命中（求值时已断言环境变量值满足条件）</summary>
        public bool Matched { get; set; }

        /// <summary>报警文案（默认模板或规则 Message）</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 报警规则内存快照（引擎加载后的不可变只读字段），供求值时使用。
    /// </summary>
    public class AlarmRuleSnapshot
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int DeviceId { get; init; }
        public string VariableKey { get; init; } = string.Empty;
        public Domain.Enums.TriggerConditionEnum Condition { get; init; }
        public double Threshold { get; init; }
        public Domain.Enums.AlarmLevelEnum Level { get; init; }
        public string? Message { get; init; }
        public int DebounceSeconds { get; init; }
    }

    /// <summary>
    /// 报警规则引擎：加载/热重载活跃报警规则快照，并按设备+变量查询、求值。
    /// <para>
    /// 引擎为 Singleton，规则数据以不可变快照保存，写侧整体替换，保证采集线程并发读安全。
    /// 规则 CRUD 后通过 <see cref="ReloadAsync"/> 热重载（默认由内置定时任务周期性下拉最新规则）。
    /// </para>
    /// </summary>
    public interface IAlarmRuleEngine
    {
        /// <summary>
        /// 重新加载活跃报警规则（CRUD 变更后调用；内部线程安全）。
        /// </summary>
        Task ReloadAsync();

        /// <summary>
        /// 获取指定设备下某变量的活跃报警规则（未命中则为空列表）。
        /// </summary>
        IReadOnlyList<AlarmRuleSnapshot> GetRules(int deviceId, string variableKey);

        /// <summary>
        /// 引擎已加载的活动规则数（供诊断）。
        /// </summary>
        int LoadedRuleCount { get; }
    }
}