using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 区域 DTO（设备分区的名称与描述）。
    /// </summary>
    public class AreaDto
    {
        /// <summary>区域ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>区域名称；必填，最长 50 字符（校验特性）</summary>
        [Required(ErrorMessage = "区域名称不能为空")]
        [StringLength(50, ErrorMessage = "区域名称不能超过50个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>区域描述；可空，最长 200 字符（校验特性）</summary>
        [StringLength(200, ErrorMessage = "描述不能超过200个字符")]
        public string Description { get; set; } = string.Empty;
    }
}
