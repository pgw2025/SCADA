using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 设备运行时状态实体 - 仅内存维护，不持久化到数据库
    /// </summary>
    /// <remarks>
    /// 该实体用于跟踪设备在运行时的状态信息，包括连接状态、通信统计、错误信息等。
    /// 数据仅在内存中维护，服务重启后会重新初始化。
    /// </remarks>
    public class DeviceRuntime
    {
        /// <summary>
        /// 设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 设备名称（缓存，避免频繁查询数据库）
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// 当前设备状态（在线/离线/错误等）
        /// </summary>
        public DeviceStatus CurrentStatus { get; set; } = DeviceStatus.Offline;

        /// <summary>
        /// 最后一次心跳时间
        /// </summary>
        public DateTime? LastHeartbeat { get; set; }

        /// <summary>
        /// 重连次数（设备断开后的自动重连尝试次数）
        /// </summary>
        public int ReconnectCount { get; set; }

        /// <summary>
        /// 最后一次错误信息
        /// </summary>
        public string? LastError { get; set; }

        /// <summary>
        /// 最后一次成功通信时间
        /// </summary>
        public DateTime? LastCommunicationTime { get; set; }

        /// <summary>
        /// 是否正在连接中
        /// </summary>
        public bool IsConnecting { get; set; }

        /// <summary>
        /// 设备运行时长（秒）
        /// </summary>
        public long UptimeSeconds { get; set; }

        /// <summary>
        /// 采集统计：成功次数
        /// </summary>
        public long SuccessCount { get; set; }

        /// <summary>
        /// 采集统计：失败次数
        /// </summary>
        public long FailureCount { get; set; }
    }
}
