using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 对外暴露接口 DTO（将设备变量以外部可访问的接口形式暴露给第三方）。
    /// </summary>
    public class ExposedInterfaceDto
    {
        /// <summary>接口ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>接口名称</summary>
        [Required(ErrorMessage = "接口名称不能为空")]
        [StringLength(100, ErrorMessage = "接口名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>对外可访问的路由地址</summary>
        [Required(ErrorMessage = "路由地址不能为空")]
        [StringLength(200, ErrorMessage = "路由地址不能超过200个字符")]
        public string RouteUrl { get; set; } = string.Empty;

        /// <summary>HTTP 请求方法（如 GET / POST）</summary>
        [Required(ErrorMessage = "请求方法不能为空")]
        [StringLength(10, ErrorMessage = "请求方法不能超过10个字符")]
        public string RequestMethod { get; set; } = string.Empty;

        /// <summary>关联设备ID</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请选择关联设备")]
        public int DeviceId { get; set; }

        /// <summary>对外暴露的变量业务键（标识哪个变量被暴露）</summary>
        [Required(ErrorMessage = "暴露变量键不能为空")]
        [StringLength(100, ErrorMessage = "暴露变量键不能超过100个字符")]
        public string ExposedKey { get; set; } = string.Empty;

        /// <summary>是否启用该对外接口</summary>
        public bool Active { get; set; }
    }
}
