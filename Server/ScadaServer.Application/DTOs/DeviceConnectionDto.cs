using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 设备连接创建/更新请求体（阶段 3：连接参数抽取实体）。
    /// <para>
    /// 依据 P3-B：<see cref="ConfigJson"/> 保存驱动完整配置原文（即原 Device.JsonConfig），为连接配置真相源；
    /// <see cref="Host"/>/<see cref="Port"/> 为冗余列（管理/检索展示用），服务端在 <see cref="ConfigJson"/>
    /// 变更时按协议自动重算，客户端通常无需提交。
    /// </para>
    /// </summary>
    public class CreateDeviceConnectionDto
    {
        /// <summary>所属控制器 ID（FK → Controllers）。</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请选择所属控制器")]
        public int ControllerId { get; set; }

        /// <summary>连接名称。</summary>
        [Required(ErrorMessage = "连接名称不能为空")]
        [StringLength(100, ErrorMessage = "连接名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>协议 ID（FK → Protocols，S7/OPCUA/Virtual...）。</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请选择协议")]
        public int ProtocolId { get; set; }

        /// <summary>
        /// 驱动完整配置原文（即原 Device.JsonConfig，含 IP/端口/端点等，P3-B）。
        /// 更新时留空 = 保留原配置（与设备 PUT 语义一致）。
        /// </summary>
        public string? ConfigJson { get; set; }

        /// <summary>提取的 IP / 主机名（冗余列，非必填；提交 ConfigJson 时由服务端重算覆盖）。</summary>
        [StringLength(100, ErrorMessage = "主机名不能超过100个字符")]
        public string? Host { get; set; }

        /// <summary>提取的端口（冗余列，非必填；提交 ConfigJson 时由服务端重算覆盖）。</summary>
        public int? Port { get; set; }

        /// <summary>IO 超时（毫秒），默认 5000。</summary>
        [Range(100, 3600000, ErrorMessage = "IO 超时必须在100ms到1小时之间")]
        public int TimeoutMs { get; set; } = 5000;

        /// <summary>重连周期（毫秒），默认 5000。</summary>
        [Range(100, 3600000, ErrorMessage = "重连周期必须在100ms到1小时之间")]
        public int ReconnectIntervalMs { get; set; } = 5000;

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// 设备连接 DTO。对应 <see cref="ScadaServer.Domain.Entities.DeviceConnection"/> 实体。
    /// </summary>
    public class DeviceConnectionDto : CreateDeviceConnectionDto
    {
        /// <summary>连接 ID（主键，创建时由服务端生成）。</summary>
        public int Id { get; set; }

        /// <summary>控制器编码（派生展示字段，来自 Controller 导航）。</summary>
        public string? ControllerCode { get; set; }

        /// <summary>控制器名称（派生展示字段，来自 Controller 导航）。</summary>
        public string? ControllerName { get; set; }

        /// <summary>协议名称（派生展示字段，来自 Protocol 导航）。</summary>
        public string? ProtocolName { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>最近更新时间</summary>
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 设备详情中的连接摘要（阶段 3：DeviceDto 新增只读字段，展示连接参数抽取结果）。
    /// <para>
    /// 来源为 <c>Device.Connection</c>（含其 Controller/Protocol 导航）；旧字段
    /// <c>ConfigJson</c>/<c>ProtocolKey</c> 及 ip/port 派生字段仍只读保留，兼容期前端不炸。
    /// </para>
    /// </summary>
    public class DeviceConnectionSummaryDto
    {
        /// <summary>连接 ID。</summary>
        public int Id { get; set; }

        /// <summary>所属控制器 ID。</summary>
        public int ControllerId { get; set; }

        /// <summary>控制器编码（派生展示字段）。</summary>
        public string? ControllerCode { get; set; }

        /// <summary>控制器名称（派生展示字段）。</summary>
        public string? ControllerName { get; set; }

        /// <summary>协议 ID。</summary>
        public int ProtocolId { get; set; }

        /// <summary>协议业务键（驱动派发键，派生自 Protocol.DriverKey）。</summary>
        public string? ProtocolKey { get; set; }

        /// <summary>协议显示名称（派生展示字段）。</summary>
        public string? ProtocolName { get; set; }

        /// <summary>提取的 IP / 主机名（冗余列，Virtual 为 null）。</summary>
        public string? Host { get; set; }

        /// <summary>提取的端口（冗余列，Virtual 为 null）。</summary>
        public int? Port { get; set; }

        /// <summary>IO 超时（毫秒）。</summary>
        public int TimeoutMs { get; set; }

        /// <summary>重连周期（毫秒）。</summary>
        public int ReconnectIntervalMs { get; set; }

        /// <summary>是否启用。</summary>
        public bool IsEnabled { get; set; }

        /// <summary>最近更新时间（连接参数变更监控用）。</summary>
        public DateTime UpdatedAt { get; set; }
    }
}
