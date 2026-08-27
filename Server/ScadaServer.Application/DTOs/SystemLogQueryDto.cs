namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 系统日志查询条件（分页 + 分级 + 关键字 + 时间段）
    /// </summary>
    public class SystemLogQueryDto
    {
        /// <summary>
        /// 日志分类过滤：Runtime / Operation / Security；空或 null 表示全部
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// 日志级别过滤（多选，ASP.NET Core 原生数组绑定：?levels=Warning&amp;levels=Error）
        /// </summary>
        public List<string>? Levels { get; set; }

        /// <summary>
        /// 关键字，对 Content / Source / Operator 三字段做模糊匹配
        /// </summary>
        public string? Keyword { get; set; }

        /// <summary>
        /// 来源精确过滤
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// 起始时间（含边界）
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 结束时间（含边界）
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 页码，从 1 开始
        /// </summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>
        /// 每页条数，上限 100（服务端强制夹紧）
        /// </summary>
        public int PageSize { get; set; } = 20;
    }

    /// <summary>
    /// 系统日志分页查询结果
    /// </summary>
    public class SystemLogPagedResultDto
    {
        public int Total { get; set; }
        public List<SystemLogDto> Items { get; set; } = new();
    }

    /// <summary>
    /// 系统日志批量清理条件（必须显式时间范围）
    /// </summary>
    public class SystemLogClearDto
    {
        /// <summary>
        /// 分类过滤（可选）：Runtime / Operation / Security
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// 起始时间（含边界）
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 结束时间（含边界）
        /// </summary>
        public DateTime? EndTime { get; set; }
    }
}
