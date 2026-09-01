namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 系统日志 DTO（统一承载运行/操作/安全日志）
    /// </summary>
    public class SystemLogDto
    {
        /// <summary>日志ID（主键）</summary>
        public int Id { get; set; }

        /// <summary>日志产生时间</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 日志分类：Runtime（运行）/ Operation（操作审计）/ Security（安全审计）
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// 日志级别（Trace/Debug/Information/Warning/Error/Critical）
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// 日志来源
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// 动作类型（仅操作/安全日志）
        /// </summary>
        public string? Operation { get; set; }

        /// <summary>
        /// 操作人（仅操作/安全日志）
        /// </summary>
        public string? Operator { get; set; }

        /// <summary>
        /// 客户端 IP（仅操作/安全日志）
        /// </summary>
        public string? IpAddress { get; set; }

        /// <summary>
        /// 关联对象标识
        /// </summary>
        public string? RelatedId { get; set; }

        /// <summary>
        /// 日志内容
        /// </summary>
        public string Content { get; set; } = string.Empty;
    }
}
