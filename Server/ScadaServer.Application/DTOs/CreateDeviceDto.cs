using System.ComponentModel.DataAnnotations;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    public class CreateDeviceDto
    {
        [Required(ErrorMessage = "设备名称不能为空")]
        [StringLength(100, ErrorMessage = "设备名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "设备标识不能为空")]
        [StringLength(100, ErrorMessage = "设备标识不能超过100个字符")]
        public string Key { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "请选择所属区域")]
        public int AreaId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "请选择变量模型")]
        public int ModelId { get; set; }

        /// <summary>
        /// 设备类型
        /// </summary>
        [Required(ErrorMessage = "设备类型不能为空")]
        public DeviceType Type { get; set; }

        /// <summary>
        /// 是否启用采集
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 采集周期（毫秒）
        /// </summary>
        [Range(10, 3600000, ErrorMessage = "采集周期必须在10ms到1小时之间")]
        public int PollingInterval { get; set; } = 1000;

        /// <summary>
        /// 驱动名称（可选，默认根据 Type 自动选择）
        /// </summary>
        public string? DriverName { get; set; }

        /// <summary>
        /// 协议配置（JSON 格式）
        /// S7: {"IpAddress":"192.168.1.10","Port":102,"Rack":0,"Slot":1,"CpuType":"S71500"}
        /// ModbusTcp: {"IpAddress":"192.168.1.20","Port":502,"UnitId":1}
        /// OpcUa: {"EndpointUrl":"opc.tcp://localhost:4840","SecurityPolicy":"None"}
        /// Mqtt: {"Broker":"tcp://localhost:1883","Topic":"scada/data"}
        /// </summary>
        [Required(ErrorMessage = "协议配置不能为空")]
        public string ConfigJson { get; set; } = string.Empty;
    }
}
