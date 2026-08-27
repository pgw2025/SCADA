using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 报警事件类型（触发 / 恢复）。
    /// </summary>
    public enum AlarmEventType
    {
        /// <summary>触发（进入报警状态）</summary>
        Triggered,

        /// <summary>恢复（退出报警状态）</summary>
        Recovered
    }

    /// <summary>
    /// 运行时报警事件，由报警检测（规则引擎命中或 Min/Max 兜底、系统级）产生。
    /// <para>
    /// 该对象被 <see cref="Interfaces.IScadaNotificationService"/>（SignalR 推送）与
    /// <see cref="Interfaces.IAlarmRecorder"/>（异步落库）共同消费；
    /// 前端通过 SignalR "ReceiveAlarm" 接收同构对象。
    /// </para>
    /// </summary>
    public class AlarmEvent
    {
        /// <summary>事件类型（触发/恢复）</summary>
        public AlarmEventType EventType { get; set; }

        /// <summary>所属设备ID</summary>
        public int DeviceId { get; set; }

        /// <summary>设备标识</summary>
        public string DeviceKey { get; set; } = string.Empty;

        /// <summary>变量业务键</summary>
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>变量名称</summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>命中的规则ID（规则告警有值；兜底为空）</summary>
        public long? RuleId { get; set; }

        /// <summary>规则名称</summary>
        public string? RuleName { get; set; }

        /// <summary>报警级别</summary>
        public AlarmLevelEnum Level { get; set; }

        /// <summary>触发的比较条件（规则告警有值；兜底为空）</summary>
        public TriggerConditionEnum? Condition { get; set; }

        /// <summary>阈值（规则告警有值）</summary>
        public double? Threshold { get; set; }

        /// <summary>实际值字符串</summary>
        public string? ActualValue { get; set; }

        /// <summary>报警文案</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>报警来源（Rule / MinMaxLimit / System）</summary>
        public AlarmSourceEnum Source { get; set; }

        /// <summary>事件时间</summary>
        public DateTime TriggeredAt { get; set; }
    }
}