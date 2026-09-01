using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// MQTT 服务器的实时状态（用于前端卡片状态展示，由 MqttManager 维护）。
    /// </summary>
    public class MqttServerStatusDto
    {
        /// <summary>服务器配置ID</summary>
        public int Id { get; set; }

        /// <summary>
        /// 服务器名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 连接状态：Connected / Connecting / Disconnected / Error / Disabled
        /// </summary>
        public string Status { get; set; } = "Disconnected";

        /// <summary>
        /// 最近一次错误信息（无则空字符串）
        /// </summary>
        public string LastError { get; set; } = string.Empty;

        /// <summary>
        /// 最近一次成功连接时间（UTC，从未连通过为 null）
        /// </summary>
        public DateTime? LastConnectedUtc { get; set; }

        /// <summary>
        /// 当前累计重连尝试次数
        /// </summary>
        public int ReconnectAttempts { get; set; }

        /// <summary>
        /// 该服务器下已关联的变量数量
        /// </summary>
        public int VariableCount { get; set; }
    }

    /// <summary>
    /// MQTT 测试连接结果。
    /// </summary>
    public class MqttTestConnectionResultDto
    {
        /// <summary>连接测试是否成功</summary>
        public bool Success { get; set; }

        /// <summary>失败时的错误信息（成功时为空字符串）</summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// MQTT 变量映射（服务器关联变量），供详情页列表展示。
    /// </summary>
    public class MqttVariableConfigDto
    {
        /// <summary>映射ID（主键）</summary>
        public int Id { get; set; }

        /// <summary>所属 MQTT 服务器ID</summary>
        public int MqttServerId { get; set; }

        /// <summary>关联设备ID</summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 设备名称（联查填充）
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// 变量键（原变量名）
        /// </summary>
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 变量显示名称（联查填充）
        /// </summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>
        /// 转发别名：该服务器下此变量使用的消息 message 名（不同服务器可各不相同）
        /// </summary>
        public string Alias { get; set; } = string.Empty;

        /// <summary>
        /// 自定义主题（可选，非空时优先于「前缀/别名」拼接）
        /// </summary>
        public string? CustomTopic { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 完整推送主题预览（后端基于前缀/别名或自定义主题计算）
        /// </summary>
        public string TopicPreview { get; set; } = string.Empty;

        /// <summary>
        /// 实时值（联查实时快照填充，可能为 null）
        /// </summary>
        public object? RealtimeValue { get; set; }
    }
}