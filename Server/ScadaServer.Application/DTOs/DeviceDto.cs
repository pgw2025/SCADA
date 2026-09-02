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
        /// <para>
        /// 设备连接参数的<strong>唯一真相源</strong>。下面一组连接字段均为由本字段按
        /// <c>Protocol.DriverKey</c> 派生的只读投影，不单独持久化。
        /// </para>
        /// </summary>
        public string? ConfigJson { get; set; }

        #region 连接参数（派生只读，由 ConfigJson 投影，PUT 时被后端忽略）

        /// <summary>
        /// IP 地址 / 主机名。S7、ModbusTcp 直接取自配置；OPC UA 由 <see cref="EndpointUrl"/> 解析出主机部分。
        /// </summary>
        public string? IpAddress { get; set; }

        /// <summary>端口号（S7 默认 102 / ModbusTcp 默认 502 / MQTT 默认 1883）。</summary>
        public int? Port { get; set; }

        /// <summary>S7 CPU 型号（如 S7-1200 / S7-1500）。仅 S7 有值。</summary>
        public string? CpuType { get; set; }

        /// <summary>S7 机架号。仅 S7 有值。</summary>
        public int? Rack { get; set; }

        /// <summary>S7 槽位号。仅 S7 有值。</summary>
        public int? Slot { get; set; }

        /// <summary>
        /// OPC UA 端点地址（完整 URL，可能带路径如 <c>opc.tcp://host:4840/server</c>）。
        /// 前端编辑应原样回填本字段，不要用 ip+port 拼接，否则会丢失路径。
        /// </summary>
        public string? EndpointUrl { get; set; }

        /// <summary>ModbusTcp 从站单元地址（Unit ID）。仅 ModbusTcp 有值，预留。</summary>
        public int? UnitId { get; set; }

        /// <summary>MQTT Broker 地址。仅 MQTT 有值，预留。</summary>
        public string? Broker { get; set; }

        /// <summary>MQTT 订阅/发布主题。仅 MQTT 有值，预留。</summary>
        public string? Topic { get; set; }

        /// <summary>虚拟设备值更新间隔（毫秒）。仅 Virtual 有值。</summary>
        public int? IntervalMs { get; set; }

        /// <summary>虚拟设备是否随机产生数值。仅 Virtual 有值。</summary>
        public bool? RandomValues { get; set; }

        #endregion

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
