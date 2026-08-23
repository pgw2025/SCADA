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
        /// 模型下的变量列表
        /// </summary>
        public List<ModelVariableDto>? Variables { get; set; }
    }
}
