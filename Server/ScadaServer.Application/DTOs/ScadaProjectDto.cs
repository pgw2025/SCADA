using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 组态工程 DTO（描述一个可视化组态工程的基本信息）。
    /// </summary>
    public class ScadaProjectDto
    {
        /// <summary>工程ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>工程名称</summary>
        [Required(ErrorMessage = "工程名称不能为空")]
        [StringLength(100, ErrorMessage = "工程名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>工程描述（可空）</summary>
        [StringLength(500, ErrorMessage = "工程描述不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; }
    }
}
