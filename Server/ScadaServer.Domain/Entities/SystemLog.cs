using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 系统日志实体
    /// </summary>
    [SugarTable("SystemLogs")]
    public class SystemLog : EntityBase
    {
        /// <summary>
        /// 日志时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 日志级别（如：Debug、Info、Warning、Error）
        /// </summary>
        public string Level { get; set; }

        /// <summary>
        /// 日志来源
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// 日志内容
        /// </summary>
        public string Content { get; set; }
    }
}