using System.ComponentModel.DataAnnotations;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    public class CreateDataModelDto
    {
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
        /// 关联通信协议ID（协议真相源）。创建模型时由前端选择协议下拉得到；为空表示过渡期暂不绑定协议。
        /// </summary>
        public int? ProtocolId { get; set; }

        /// <summary>
        /// 协议类型（枚举）——过渡期兼容字段，保留以兼容旧调用；新逻辑应优先使用 <see cref="ProtocolId"/>
        /// </summary>
        public DeviceType Type { get; set; }
    }
}
