namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 历史数据记录 DTO（历史趋势曲线数据点）
    /// </summary>
    public class HistoryRecordDto
    {
        /// <summary>记录ID</summary>
        public long Id { get; set; }

        /// <summary>所属设备ID（冗余，前端区分同名变量）</summary>
        public int DeviceId { get; set; }

        /// <summary>所属设备标识（冗余，前端区分同名变量）</summary>
        public string DeviceKey { get; set; } = string.Empty;

        /// <summary>变量业务键</summary>
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>变量名称</summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>数值化后的值（数字量 0/1）</summary>
        public double Value { get; set; }

        /// <summary>原始值字符串</summary>
        public string? RawValue { get; set; }

        /// <summary>采样时间</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>采样质量（如 Good / CommunicationError）</summary>
        public string? Quality { get; set; }
    }
}
