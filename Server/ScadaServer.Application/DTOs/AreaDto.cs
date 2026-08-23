using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    public class AreaDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "区域名称不能为空")]
        [StringLength(50, ErrorMessage = "区域名称不能超过50个字符")]
        public string Name { get; set; }

        [StringLength(200, ErrorMessage = "描述不能超过200个字符")]
        public string Description { get; set; }
    }
}
