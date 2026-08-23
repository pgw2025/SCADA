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
        /// 设备类型（枚举）
        /// </summary>
        [Required(ErrorMessage = "模型类型不能为空")]
        public DeviceType Type { get; set; }

        /// <summary>
        /// 模型下的变量列表
        /// </summary>
        public List<ModelVariableDto>? Variables { get; set; }
    }
}
