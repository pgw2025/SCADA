namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 报警来源（区分规则命中、Min/Max 上下限兜底与系统级告警）。
    /// </summary>
    public enum AlarmSourceEnum
    {
        /// <summary>规则命中（AlarmRule 求值触发）</summary>
        Rule,

        /// <summary>Min/Max 上下限越界兜底</summary>
        MinMaxLimit,

        /// <summary>系统级告警（如绑定环路检测）</summary>
        System
    }
}