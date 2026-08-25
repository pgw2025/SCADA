using System.ComponentModel.DataAnnotations;

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
        /// 关联通信协议ID（协议真相源，必填）。创建模型时必须选择协议；更新时必须原样回传，避免解绑。
        /// </summary>
        [Required(ErrorMessage = "请选择通信协议")]
        public int ProtocolId { get; set; }

        /// <summary>
        /// 协议业务键（只读，来自 <see cref="ProtocolId"/> 关联的 <c>Protocol.Key</c>）
        /// </summary>
        public string? ProtocolKey { get; set; }

        /// <summary>
        /// 协议显示名称（只读，来自 <see cref="ProtocolId"/> 关联的 <c>Protocol.Name</c>）
        /// </summary>
        public string? ProtocolName { get; set; }

        /// <summary>
        /// 模型下的变量列表
        /// </summary>
        public List<ModelVariableDto>? Variables { get; set; }
    }
}
