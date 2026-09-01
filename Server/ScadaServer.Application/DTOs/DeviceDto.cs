using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 设备 DTO（返回给前端，含所属区域/模型、协议信息与运行时状态）。
    /// </summary>
    public class DeviceDto
    {
        /// <summary>设备ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>设备名称；必填，最长 100 字符（校验特性）</summary>
        [Required(ErrorMessage = "设备名称不能为空")]
        [StringLength(100, ErrorMessage = "设备名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>设备标识（全局唯一）；必填，最长 100 字符（校验特性）</summary>
        [Required(ErrorMessage = "设备标识不能为空")]
        [StringLength(100, ErrorMessage = "设备标识不能超过100个字符")]
        public string Key { get; set; } = string.Empty;

        /// <summary>所属区域ID；必填，范围需大于 0（校验特性）</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请选择所属区域")]
        public int AreaId { get; set; }

        /// <summary>所属区域名称（只读，来自区域表）</summary>
        public string? AreaName { get; set; }

        /// <summary>绑定的数据模型ID；必填，范围需大于 0（校验特性）</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请选择变量模型")]
        public int ModelId { get; set; }

        /// <summary>绑定的数据模型名称（只读）</summary>
        public string? ModelName { get; set; }

        /// <summary>
        /// 协议业务键（只读，来自所绑定数据模型的 <c>Protocol.Key</c>）。协议真相源。
        /// </summary>
        public string? ProtocolKey { get; set; }

        /// <summary>
        /// 协议显示名称（只读，来自所绑定数据模型的 <c>Protocol.Name</c>）。
        /// </summary>
        public string? ProtocolName { get; set; }

        /// <summary>
        /// 是否启用采集
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 采集周期（毫秒）
        /// </summary>
        [Range(10, 3600000, ErrorMessage = "采集周期必须在10ms到1小时之间")]
        public int PollingInterval { get; set; } = 1000;

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>最近更新时间</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>最近一次通信时间（可空，尚未通信为空）</summary>
        public DateTime? LastCommunicationTime { get; set; }

        /// <summary>
        /// 协议配置（JSON）
        /// </summary>
        public string? ConfigJson { get; set; }

        /// <summary>
        /// 运行时状态（仅查询时返回）。
        /// <para>
        /// 以字符串枚举输出（Online/Offline/Fault/Connecting/ConfigUpdating），
        /// 与 SignalR 推送的字符串状态一致，保证 REST 与实时两条链路前端映射统一。
        /// </para>
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DeviceStatus? RuntimeStatus { get; set; }

        /// <summary>
        /// 设备下的变量列表（聚合变量模板定义与设备实例配置）。
        /// <para>新增字段：前端可据此展示每个变量的"定义 + 设备配置"；既有接口字段不受影响，旧客户端可忽略。</para>
        /// </summary>
        public List<DeviceVariableDto>? Variables { get; set; }
    }
}
