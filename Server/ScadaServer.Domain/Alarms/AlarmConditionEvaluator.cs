using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Alarms
{
    /// <summary>
    /// 报警条件求值器（领域纯函数，唯一权威）。
    /// <para>
    /// 条件比较逻辑原为 <c>DeviceWorker.MatchesCondition</c> 的私有静态方法；因服务重启巡检
    /// （AlarmRecoveryStartupService）也需要按报警记录中固化的 Condition/Threshold 判定当前值
    /// 是否已脱离报警，故平移到此处共享，避免两处判定逻辑分叉。浮点相等使用容差避免精度抖动。
    /// </para>
    /// </summary>
    public static class AlarmConditionEvaluator
    {
        /// <summary>
        /// 判定给定数值是否命中条件（进入报警）；未命中即代表已脱离报警。
        /// </summary>
        /// <param name="condition">比较条件（GreaterThan / GreaterOrEqual / LessThan / LessOrEqual / EqualTo / NotEqualTo）。</param>
        /// <param name="value">当前实时值。</param>
        /// <param name="threshold">规则阈值。</param>
        public static bool IsMatched(TriggerConditionEnum condition, double value, double threshold)
        {
            const double epsilon = 1e-9;
            return condition switch
            {
                TriggerConditionEnum.GreaterThan => value > threshold,
                TriggerConditionEnum.GreaterOrEqual => value >= threshold,
                TriggerConditionEnum.LessThan => value < threshold,
                TriggerConditionEnum.LessOrEqual => value <= threshold,
                TriggerConditionEnum.EqualTo => Math.Abs(value - threshold) <= epsilon,
                TriggerConditionEnum.NotEqualTo => Math.Abs(value - threshold) > epsilon,
                _ => false
            };
        }
    }
}
