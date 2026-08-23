using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 系统脚本实体
    /// </summary>
    [SugarTable("SystemScripts")]
    public class SystemScript : EntityBase
    {
        /// <summary>
        /// 脚本名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 脚本代码内容
        /// </summary>
        [SugarColumn(ColumnDataType = "text")]
        public string Code { get; set; }

        /// <summary>
        /// 触发类型（如：定时、事件、手动）
        /// </summary>
        public string TriggerType { get; set; }

        /// <summary>
        /// 执行间隔（秒），定时触发时使用
        /// </summary>
        public int? IntervalSeconds { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; }
    }
}