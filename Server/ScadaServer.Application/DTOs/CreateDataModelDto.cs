using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 创建设备数据模型 DTO（描述设备型号与关联协议，用于创建时提交）。
    /// </summary>
    public class CreateDataModelDto
    {
        /// <summary>模型名称；必填，最长 100 字符（校验特性）</summary>
        [Required(ErrorMessage = "模型名称不能为空")]
        [StringLength(100, ErrorMessage = "模型名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>模型描述；可空，最长 500 字符（校验特性）</summary>
        [StringLength(500, ErrorMessage = "描述不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 设备厂商（如 "Siemens"、"Schneider"），仅作描述展示，不决定协议。
        /// </summary>
        [StringLength(100, ErrorMessage = "厂商不能超过100个字符")]
        public string? Vendor { get; set; }

        /// <summary>
        /// 设备型号名称（如 "S7-1500"、"M340"），仅作描述展示，不决定协议。
        /// </summary>
        [StringLength(100, ErrorMessage = "型号不能超过100个字符")]
        public string? ModelName { get; set; }

        /// <summary>
        /// 厂商/型号描述（如 "Siemens S7-1500"），仅供展示，不决定协议
        /// </summary>
        [StringLength(100, ErrorMessage = "厂商型号不能超过100个字符")]
        public string? VendorModel { get; set; }

        /// <summary>
        /// 关联通信协议ID（协议真相源，必填）。创建模型时必须选择协议。
        /// </summary>
        [Required(ErrorMessage = "请选择通信协议")]
        [Range(1, int.MaxValue, ErrorMessage = "请选择通信协议")]
        public int ProtocolId { get; set; }
    }
}
