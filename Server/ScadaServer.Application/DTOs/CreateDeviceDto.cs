using System.ComponentModel.DataAnnotations;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 创建设备 DTO（绑定区域与数据模型，用于创建时提交）。
    /// </summary>
    public class CreateDeviceDto
    {
        /// <summary>设备名称；必填，最长 100 字符（校验特性）</summary>
        [Required(ErrorMessage = "设备名称不能为空")]
        [StringLength(100, ErrorMessage = "设备名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 设备标识（可选）。留空时由后台根据所属区域自动生成（如 BLR-001）。
        /// 若指定则使用指定值，且必须全局唯一。
        /// </summary>
        [StringLength(100, ErrorMessage = "设备标识不能超过100个字符")]
        public string? Key { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "请选择所属区域")]
        public int AreaId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "请选择变量模型")]
        public int ModelId { get; set; }

        /// <summary>
        /// 显式附加的控制器 ID（阶段 3.6 由"高级模式"提升为唯一模式）。
        /// <para>
        /// 与 <see cref="ConnectionId"/> 成对出现且均必填：设备协议由所附连接承载
        /// （Connection.ConfigJson 为连接配置真相源，运行期按 <c>Protocol.Key</c> 派发驱动）。
        /// 已不支持快速模式（后端不再自动维护专属 Controller + Connection）。
        /// </summary>
        public int? ControllerId { get; set; }

        /// <summary>
        /// 显式附加的设备连接 ID（阶段 3.6，必填，语义见 <see cref="ControllerId"/>）。
        /// </summary>
        public int? ConnectionId { get; set; }

        /// <summary>
        /// 是否启用采集（默认停用，新增后需在设备管理页手动启用）
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// 采集周期（毫秒）
        /// </summary>
        [Range(10, 3600000, ErrorMessage = "采集周期必须在10ms到1小时之间")]
        public int PollingInterval { get; set; } = 1000;

        /// <summary>
        /// 协议配置（JSON 格式）
        /// S7: {"IpAddress":"192.168.1.10","Port":102,"Rack":0,"Slot":1,"CpuType":"S71500"}
        /// ModbusTcp: {"IpAddress":"192.168.1.20","Port":502,"UnitId":1}
        /// OpcUa: {"EndpointUrl":"opc.tcp://localhost:4840","SecurityPolicy":"None"}
        /// Mqtt: {"Broker":"tcp://localhost:1883","Topic":"scada/data"}
        /// <para>仅高级模式：连接配置以所附 Connection.ConfigJson 为真相源，本字段创建设备时被忽略（保留兼容字段）。</para>
        /// </summary>
        public string ConfigJson { get; set; } = string.Empty;
    }
}
