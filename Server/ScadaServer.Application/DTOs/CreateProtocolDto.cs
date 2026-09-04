using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 创建通信协议 DTO。
    /// </summary>
    public class CreateProtocolDto
    {
        /// <summary>协议业务键（稳定标识符，如 "s7"、"opcua"、"virtual"），全局唯一。</summary>
        [Required(ErrorMessage = "协议键不能为空")]
        [StringLength(50, ErrorMessage = "协议键不能超过50个字符")]
        public string Key { get; set; } = string.Empty;

        /// <summary>协议显示名称（如 "Siemens S7"、"OPC UA"）。</summary>
        [Required(ErrorMessage = "协议名称不能为空")]
        [StringLength(100, ErrorMessage = "协议名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>协议描述信息。</summary>
        [StringLength(500, ErrorMessage = "描述不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>是否启用，默认 true。</summary>
        public bool IsEnabled { get; set; } = true;
    }
}