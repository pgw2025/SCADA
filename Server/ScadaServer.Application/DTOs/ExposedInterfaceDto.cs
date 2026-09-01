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
        public string Name { get; set; } = string.Empty;

        /// <summary>对外可访问的路由地址</summary>
        public string RouteUrl { get; set; } = string.Empty;

        /// <summary>HTTP 请求方法（如 GET / POST）</summary>
        public string RequestMethod { get; set; } = string.Empty;

        /// <summary>关联设备ID</summary>
        public int DeviceId { get; set; }

        /// <summary>对外暴露的变量业务键（标识哪个变量被暴露）</summary>
        public string ExposedKey { get; set; } = string.Empty;

        /// <summary>是否启用该对外接口</summary>
        public bool Active { get; set; }
    }
}
