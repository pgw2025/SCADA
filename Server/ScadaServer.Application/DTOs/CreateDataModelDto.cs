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

        /// <summary>
        /// 模型编码（业务唯一键，阶段 4 新增）；必填且全局唯一（应用层校验），最长 100 字符。
        /// </summary>
        [Required(ErrorMessage = "模型编码不能为空")]
        [StringLength(100, ErrorMessage = "模型编码不能超过100个字符")]
        public string Code { get; set; } = string.Empty;

        /// <summary>模型版本号，默认 "1.0"；最长 20 字符</summary>
        [StringLength(20, ErrorMessage = "版本号不能超过20个字符")]
        public string Version { get; set; } = "1.0";

        /// <summary>是否已发布（标识模型是否可被新建设备引用），默认 true</summary>
        public bool IsPublished { get; set; } = true;

        /// <summary>模型描述；可空，最长 500 字符（校验特性）</summary>
        [StringLength(500, ErrorMessage = "描述不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 设备厂商（如 "Siemens"、"Schneider"），仅作描述展示，不决定协议。
        /// </summary>
        [StringLength(100, ErrorMessage = "厂商不能超过100个字符")]
        public string? Vendor { get; set; }

        /// <summary>
        /// 厂商/型号描述（如 "Siemens S7-1500"），仅供展示，不决定协议
        /// </summary>
        [StringLength(100, ErrorMessage = "厂商型号不能超过100个字符")]
        public string? VendorModel { get; set; }
    }
}
