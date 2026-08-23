using System.ComponentModel.DataAnnotations;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    public class DeviceDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "设备名称不能为空")]
        [StringLength(100, ErrorMessage = "设备名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "设备标识不能为空")]
        [StringLength(100, ErrorMessage = "设备标识不能超过100个字符")]
        public string Key { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "请选择所属区域")]
        public int AreaId { get; set; }
        public string? AreaName { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "请选择变量模型")]
        public int ModelId { get; set; }
        public string? ModelName { get; set; }

        /// <summary>
        /// 设备类型（枚举）
        /// </summary>
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
        /// 驱动名称
        /// </summary>
        public string? DriverName { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastCommunicationTime { get; set; }

        /// <summary>
        /// 协议配置（JSON）
        /// </summary>
        public string? ConfigJson { get; set; }

        /// <summary>
        /// 运行时状态（仅查询时返回）
        /// </summary>
        public DeviceStatus? RuntimeStatus { get; set; }
    }
}
