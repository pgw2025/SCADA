using System.ComponentModel.DataAnnotations;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    public class DataModelDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "模型名称不能为空")]
        [StringLength(100, ErrorMessage = "模型名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "描述不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 厂商/型号描述（如 "Siemens S7-1500"），仅供展示，不决定协议
        /// </summary>
        [StringLength(100, ErrorMessage = "厂商型号不能超过100个字符")]
        public string? VendorModel { get; set; }

        /// <summary>
        /// 关联通信协议ID（协议真相源）。创建模型时由前端选择协议下拉得到。
        /// </summary>
        public int? ProtocolId { get; set; }

        /// <summary>
        /// 协议业务键（只读，来自 <see cref="ProtocolId"/> 关联的 <c>Protocol.Key</c>）
        /// </summary>
        public string? ProtocolKey { get; set; }

        /// <summary>
        /// 协议显示名称（只读，来自 <see cref="ProtocolId"/> 关联的 <c>Protocol.Name</c>）
        /// </summary>
        public string? ProtocolName { get; set; }

        /// <summary>
        /// 协议类型（枚举）——过渡期兼容字段，保留以兼容旧调用；新逻辑应优先使用 <see cref="ProtocolId"/>
        /// </summary>
        public DeviceType Type { get; set; }

        /// <summary>
        /// 模型下的变量列表
        /// </summary>
        public List<ModelVariableDto>? Variables { get; set; }
    }
}
