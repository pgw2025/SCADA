using ScadaServer.Application.Options;
using ScadaServer.WebApi.HostedServices;

namespace ScadaServer.WebApi.Services
{
    /// <summary>
    /// 操作日志审计服务接口。
    /// </summary>
    public interface IOperationAuditService
    {
        /// <summary>
        /// 记录一条操作日志（自动填充操作人 / 客户端 IP / 成功级别）。
        /// </summary>
        /// <param name="module">业务模块名（来源），如「设备管理」</param>
        /// <param name="operation">动作类型：CREATE/UPDATE/DELETE/EXECUTE/ENABLE/DISABLE 等</param>
        /// <param name="relatedId">关联对象标识（设备ID/用户ID/页面ID等）</param>
        /// <param name="description">操作描述</param>
        /// <param name="category">日志分类，默认 Operation；安全相关传 Security</param>
        Task RecordAsync(string module, string operation, string? relatedId, string description, string category = "Operation");

        /// <summary>
        /// 记录一条指定级别的操作/安全日志（供登录失败等非 2xx 场景使用）。
        /// </summary>
        Task RecordAsync(string module, string operation, string? relatedId, string description, string level, string category = "Operation");

        /// <summary>
        /// 记录一条操作日志，显式指定操作人（供 [AllowAnonymous] 的登录等无 JWT 场景使用）。
        /// </summary>
        Task RecordAsync(string module, string operation, string? relatedId, string description, string level, string operatorName, string category = "Operation");
    }

    /// <summary>
    /// 操作日志审计服务实现。
    /// <para>
    /// 依赖 WebApi 层的 <see cref="SystemLogRecorder"/>（双通道：操作日志走无界队列，不丢弃），
    /// 并通过 IHttpContextAccessor 从 JWT / 连接信息自动提取操作人与客户端 IP。
    /// </para>
    /// </summary>
    public class OperationAuditService : IOperationAuditService
    {
        private readonly SystemLogRecorder _recorder;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly int _maxContentLength;

        public OperationAuditService(
            SystemLogRecorder recorder,
            IHttpContextAccessor httpContextAccessor,
            Microsoft.Extensions.Options.IOptions<SystemLogOptions> options)
        {
            _recorder = recorder;
            _httpContextAccessor = httpContextAccessor;
            _maxContentLength = options.Value.MaxContentLength;
        }

        /// <inheritdoc/>
        public Task RecordAsync(string module, string operation, string? relatedId, string description, string category = "Operation")
            => RecordAsync(module, operation, relatedId, description, "Information", category);

        /// <inheritdoc/>
        public Task RecordAsync(string module, string operation, string? relatedId, string description, string level, string category = "Operation")
            => RecordAsync(module, operation, relatedId, description, level, ResolveOperator(), category);

        /// <inheritdoc/>
        public Task RecordAsync(string module, string operation, string? relatedId, string description, string level, string operatorName, string category = "Operation")
        {
            var context = _httpContextAccessor.HttpContext;

            // 客户端 IP：优先 X-Forwarded-For（部署在反代后），否则取连接远端地址
            var ip = context?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
            if (context?.Request?.Headers.TryGetValue("X-Forwarded-For", out var forwarded) == true
                && !string.IsNullOrWhiteSpace(forwarded.ToString()))
            {
                ip = forwarded.ToString().Split(',')[0].Trim();
            }

            var content = Truncate(description);

            _recorder.RecordOperation(category, level, module, operation, operatorName, ip, relatedId, content);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 解析当前操作人：优先 JWT 的 Name claim，其次身份名，兜底 anonymous。
        /// </summary>
        private string ResolveOperator()
        {
            var context = _httpContextAccessor.HttpContext;
            return context?.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                ?? context?.User?.Identity?.Name
                ?? "anonymous";
        }

        private string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= _maxContentLength ? value : value[.._maxContentLength];
        }
    }
}
