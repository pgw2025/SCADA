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
        public int DeviceId { get; set; }

        /// <summary>操作人（用户名）</summary>
        public string Operator { get; set; } = string.Empty;

        /// <summary>变更内容描述</summary>
        public string ChangeDesc { get; set; } = string.Empty;

        /// <summary>变更发生时间</summary>
        public DateTime CreateTime { get; set; }
    }
}
