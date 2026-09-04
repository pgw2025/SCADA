using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 通信协议 DTO。对应 <see cref="ScadaServer.Domain.Entities.Protocol"/> 实体，
    /// 描述系统所支持的通信方式（如 Siemens S7、OPC UA、虚拟设备等）。
    /// 协议是"设备／数据模型如何通信"的真相源，运行时 / 驱动工厂按 <see cref="Key"/> 派发驱动。
    /// </summary>
    public class ProtocolDto
    {
        /// <summary>协议ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>协议业务键（稳定标识符，如 "s7"、"opcua"、"virtual"），全局唯一。</summary>
        [Required(ErrorMessage = "协议键不能为空")]
        [StringLength(50, ErrorMessage = "协议键不能超过50个字符")]
        public string Key { get; set; } = string.Empty;

        /// <summary>协议显示名称（如 "Siemens S7"、"OPC UA"）。</summary>
        [Required(ErrorMessage = "协议名称不能为空")]
        [StringLength(100, ErrorMessage = "协议名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>协议描述信息（厂商、适用场景等）。</summary>
        [StringLength(500, ErrorMessage = "描述不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>是否启用。禁用后运行期不应基于此协议创建驱动实例。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>最近更新时间</summary>
        public DateTime UpdatedAt { get; set; }
    }
}