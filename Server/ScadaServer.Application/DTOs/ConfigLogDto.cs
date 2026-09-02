using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 配置变更日志 DTO（记录对设备配置的操作历史）。
    /// </summary>
    public class ConfigLogDto
    {
        /// <summary>日志ID（主键）</summary>
        public int Id { get; set; }

        /// <summary>被操作的设备ID</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请指定被操作的设备")]
        public int DeviceId { get; set; }

        /// <summary>操作人（用户名）</summary>
        [Required(ErrorMessage = "操作人不能为空")]
        [StringLength(50, ErrorMessage = "操作人不能超过50个字符")]
        public string Operator { get; set; } = string.Empty;

        /// <summary>变更内容描述</summary>
        [Required(ErrorMessage = "变更描述不能为空")]
        [StringLength(500, ErrorMessage = "变更描述不能超过500个字符")]
        public string ChangeDesc { get; set; } = string.Empty;

        /// <summary>变更发生时间</summary>
        public DateTime CreateTime { get; set; }
    }
}
