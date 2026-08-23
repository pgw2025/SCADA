using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 变量触发器实体
    /// </summary>
    [SugarTable("VariableTriggers")]
    public class VariableTrigger : EntityBase
    {
        /// <summary>
        /// 触发器名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 关联的设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 监控的变量键
        /// </summary>
        public string VariableKey { get; set; }

        /// <summary>
        /// 触发条件（如：大于、小于、等于）
        /// </summary>
        public string Condition { get; set; }

        /// <summary>
        /// 阈值
        /// </summary>
        public double Threshold { get; set; }

        /// <summary>
        /// 动作类型（如：报警、联动）
        /// </summary>
        public string ActionType { get; set; }

        /// <summary>
        /// 报警级别（如：低、中、高、紧急）
        /// </summary>
        public string AlarmLevel { get; set; }

        /// <summary>
        /// 联动变量键
        /// </summary>
        public string LinkageVariableKey { get; set; }

        /// <summary>
        /// 联动值
        /// </summary>
        public string LinkageValue { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; }
    }
}