using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 报警记录查询条件（分页 + 设备/级别/确认/恢复/时间段过滤）。
    /// </summary>
    public class AlarmRecordQueryDto
    {
        /// <summary>
        /// 设备ID过滤；null 表示全部
        /// </summary>
        public int? DeviceId { get; set; }

        /// <summary>
        /// 报警级别过滤；null 表示全部
        /// </summary>
        public AlarmLevelEnum? Level { get; set; }

        /// <summary>
        /// 是否只查未确认；null 表示全部
        /// </summary>
        public bool? Unacked { get; set; }

        /// <summary>
        /// 是否只查未恢复；null 表示全部
        /// </summary>
        public bool? Unrecovered { get; set; }

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
    /// 报警记录分页查询结果
    /// </summary>
    public class AlarmRecordPagedResultDto
    {
        public int Total { get; set; }
        public List<AlarmRecordDto> Items { get; set; } = new();
    }
}