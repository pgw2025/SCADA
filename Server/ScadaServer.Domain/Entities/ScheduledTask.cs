using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 定时任务实体
    /// </summary>
    [SugarTable("ScheduledTasks")]
    public class ScheduledTask : EntityBase
    {
        /// <summary>
        /// 任务名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 任务类型
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Cron 表达式，用于定义任务执行时间
        /// </summary>
        public string CronExpression { get; set; }

        /// <summary>
        /// 任务参数（JSON格式）
        /// </summary>
        [SugarColumn(ColumnDataType = "text")]
        public string ParamsJson { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; }
    }
}