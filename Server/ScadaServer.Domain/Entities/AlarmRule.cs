using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 报警规则实体
    /// </summary>
    [SugarTable("AlarmRules")]
    public class AlarmRule : EntityBase
    {
        /// <summary>
        /// 关联的传感器ID
        /// </summary>
        public int SensorId { get; set; }

        /// <summary>
        /// 触发条件（如：大于、小于、等于）
        /// </summary>
        public string Condition { get; set; }

        /// <summary>
        /// 阈值
        /// </summary>
        public double Threshold { get; set; }

        /// <summary>
        /// 严重程度（如：低、中、高、紧急）
        /// </summary>
        public string Severity { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }
    }
}