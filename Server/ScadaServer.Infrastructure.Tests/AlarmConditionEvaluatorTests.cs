using ScadaServer.Domain.Alarms;
using ScadaServer.Domain.Enums;
using Xunit;

namespace ScadaServer.Infrastructure.Tests
{
    /// <summary>
    /// AlarmConditionEvaluator.IsMatched 纯逻辑测试（阶段 7.5 决策 D5=A：随批补入）。
    /// <para>
    /// 覆盖六种比较条件与阈值边界：浮点相等采用 epsilon=1e-9 容差，
    /// 保证 DeviceWorker 规则路径与 AlarmRecoveryStartupService 巡检恢复路径判定一致。
    /// </para>
    /// </summary>
    public class AlarmConditionEvaluatorTests
    {
        // ---------- GreaterThan ----------

        [Fact]
        public void GreaterThan_ValueAboveThreshold_Matched() =>
            Assert.True(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.GreaterThan, 10.5, 10.0));

        [Fact]
        public void GreaterThan_ValueEqualsThreshold_NotMatched() =>
            Assert.False(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.GreaterThan, 10.0, 10.0));

        [Fact]
        public void GreaterThan_ValueBelowThreshold_NotMatched() =>
            Assert.False(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.GreaterThan, 9.9, 10.0));

        // ---------- GreaterOrEqual ----------

        [Fact]
        public void GreaterOrEqual_ValueEqualsThreshold_Matched() =>
            Assert.True(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.GreaterOrEqual, 10.0, 10.0));

        [Fact]
        public void GreaterOrEqual_ValueBelowThreshold_NotMatched() =>
            Assert.False(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.GreaterOrEqual, 9.99, 10.0));

        // ---------- LessThan ----------

        [Fact]
        public void LessThan_ValueBelowThreshold_Matched() =>
            Assert.True(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.LessThan, 9.9, 10.0));

        [Fact]
        public void LessThan_ValueEqualsThreshold_NotMatched() =>
            Assert.False(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.LessThan, 10.0, 10.0));

        // ---------- LessOrEqual ----------

        [Fact]
        public void LessOrEqual_ValueEqualsThreshold_Matched() =>
            Assert.True(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.LessOrEqual, 10.0, 10.0));

        [Fact]
        public void LessOrEqual_ValueAboveThreshold_NotMatched() =>
            Assert.False(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.LessOrEqual, 10.01, 10.0));

        // ---------- EqualTo（epsilon 容差） ----------

        [Fact]
        public void EqualTo_ExactEqual_Matched() =>
            Assert.True(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.EqualTo, 3.14, 3.14));

        [Fact]
        public void EqualTo_WithinEpsilon_Matched() =>
            Assert.True(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.EqualTo, 3.14 + 5e-10, 3.14));

        [Fact]
        public void EqualTo_BeyondEpsilon_NotMatched() =>
            Assert.False(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.EqualTo, 3.14 + 1e-6, 3.14));

        // ---------- NotEqualTo（epsilon 容差） ----------

        [Fact]
        public void NotEqualTo_DifferentValue_Matched() =>
            Assert.True(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.NotEqualTo, 3.14 + 1e-6, 3.14));

        [Fact]
        public void NotEqualTo_ExactEqual_NotMatched() =>
            Assert.False(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.NotEqualTo, 3.14, 3.14));

        [Fact]
        public void NotEqualTo_WithinEpsilon_NotMatched() =>
            Assert.False(AlarmConditionEvaluator.IsMatched(TriggerConditionEnum.NotEqualTo, 3.14 + 5e-10, 3.14));

        // ---------- 防御 ----------

        [Fact]
        public void UnknownCondition_False() =>
            Assert.False(AlarmConditionEvaluator.IsMatched((TriggerConditionEnum)999, 1.0, 1.0));
    }
}
