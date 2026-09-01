using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 系统日志实体（统一承载 运行日志 / 操作日志 / 安全日志）。
    /// </summary>
    /// <remarks>
    /// Category 用于区分日志大类：Runtime（运行）/ Operation（操作审计）/ Security（安全审计）。
    /// Level 统一为 .NET LogLevel 名称（Trace/Debug/Information/Warning/Error/Critical）。
    /// 需索引或精确匹配的列（Category/Level/Source/Operation/Operator/IpAddress/RelatedId）
    /// 均显式限制长度映射为 varchar，避免 Pomelo 默认 longtext 导致无法建索引。
    /// </remarks>
    [Table("SystemLogs")]
    public class SystemLog : EntityBase
    {
        /// <summary>
        /// 日志时间戳（UTC 存储；前端展示时本地化）
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 日志分类：Runtime（运行）/ Operation（操作审计）/ Security（安全审计）
        /// </summary>
        public string Category { get; set; } = "Runtime";

        /// <summary>
        /// 日志级别（.NET LogLevel 名称：Trace/Debug/Information/Warning/Error/Critical）
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// 日志来源（Logger category 或业务模块名，如 "ScadaServer.WebApi.Program" / "设备管理"）
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// 动作类型（仅操作/安全日志）：LOGIN/LOGOUT/CREATE/UPDATE/DELETE/EXECUTE/ENABLE/DISABLE 等；运行日志为空
        /// </summary>
        public string? Operation { get; set; }

        /// <summary>
        /// 操作人（仅操作/安全日志，来自 JWT 或登录请求体用户名）；运行日志为空
        /// </summary>
        public string? Operator { get; set; }

        /// <summary>
        /// 客户端 IP（仅操作/安全日志）；运行日志为空
        /// </summary>
        public string? IpAddress { get; set; }

        /// <summary>
        /// 关联对象标识（设备ID/用户ID/页面ID等），便于按对象追溯；可空
        /// </summary>
        public string? RelatedId { get; set; }

        /// <summary>
        /// 日志内容（写入时截断至 MaxContentLength，默认 2000 字符）
        /// </summary>
        public string Content { get; set; } = string.Empty;
    }
}
