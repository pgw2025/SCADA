using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using ScadaServer.WebApi.Services;

namespace ScadaServer.WebApi.Filters
{
    /// <summary>
    /// 操作日志审计特性：标注在控制器写方法上，方法执行后自动记录一条操作日志。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 使用 [AttributeUsage(Method)] + TypeFilterAttribute，使 <see cref="AuditLogActionFilter"/>
    /// 从 DI 容器按请求解析（依赖 IOperationAuditService / ILogger，非单例特性）。
    /// </para>
    /// <para>
    /// 审计判定口径：命中特性的写请求【一律记录】，级别按结果分档：
    /// 异常 → Error；2xx → Information；其他（4xx/5xx 业务失败）→ Warning。
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method)]
    public class AuditLogAttribute : TypeFilterAttribute
    {
        /// <summary>
        /// 初始化审计特性。
        /// </summary>
        /// <param name="module">业务模块名（来源），如「设备管理」</param>
        /// <param name="operation">动作类型，如 CREATE/UPDATE/DELETE</param>
        /// <param name="category">日志分类，默认 Operation</param>
        public AuditLogAttribute(string module, string operation, string category = "Operation")
            : base(typeof(AuditLogActionFilter))
        {
            Arguments = new object[] { module, operation, category };
        }
    }

    /// <summary>
    /// 审计过滤器实现。
    /// </summary>
    public class AuditLogActionFilter : IAsyncActionFilter
    {
        private readonly IOperationAuditService _audit;
        private readonly Microsoft.Extensions.Logging.ILogger<AuditLogActionFilter> _logger;
        private readonly string _module;
        private readonly string _operation;
        private readonly string _category;

        public AuditLogActionFilter(
            IOperationAuditService audit,
            Microsoft.Extensions.Logging.ILogger<AuditLogActionFilter> logger,
            string module,
            string operation,
            string category)
        {
            _audit = audit;
            _logger = logger;
            _module = module;
            _operation = operation;
            _category = category;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // 先记录动作描述（方法名 + 关键路由参数），供结果分档后随日志输出
            var relatedId = ExtractRelatedId(context);
            var actionName = context.ActionDescriptor.RouteValues.TryGetValue("action", out var action)
                ? action
                : context.ActionDescriptor.DisplayName ?? "action";
            // 业务上下文（如变量键 + 写入值），追加到操作描述，便于审计定位"谁改了哪个变量、改成了什么"
            var contextSuffix = ExtractContextSuffix(context);

            ActionExecutedContext executed;
            try
            {
                executed = await next();
            }
            catch (Exception ex)
            {
                // 异常：记 Error 级审计（异常仍向上抛，由全局异常中间件处理）
                await TryRecordAsync(relatedId, $"操作异常：{actionName} {ex.Message}{contextSuffix}", "Error");
                throw;
            }

            if (executed.Exception != null)
            {
                await TryRecordAsync(relatedId, $"操作异常：{actionName} {executed.Exception.Message}{contextSuffix}", "Error");
                return;
            }

            // 2xx 视为成功（Information），其余（4xx/5xx 业务失败）记为 Warning
            var statusCode = executed.HttpContext.Response.StatusCode;
            var level = statusCode is >= 200 and < 300 ? "Information" : "Warning";
            await TryRecordAsync(relatedId, $"操作完成：{actionName}（HTTP {statusCode}）{contextSuffix}", level);
        }

        /// <summary>
        /// 从路由值中提取关联对象 ID（常见 id / 数字路由参数），无则空。
        /// </summary>
        private static string? ExtractRelatedId(ActionExecutingContext context)
        {
            foreach (var key in new[] { "id", "deviceId", "userId", "pageId", "projectId" })
            {
                if (context.RouteData.Values.TryGetValue(key, out var value) && value != null)
                {
                    return value.ToString();
                }
            }
            return null;
        }

        /// <summary>
        /// 提取业务上下文后缀（变量键 + 写入值），用于操作描述中携带关键审计信息。
        /// <para>
        /// 仅识别路由值 variableKey 与请求体参数的 Value 属性（如 WriteVariableRequestDto），
        /// 其余动作（无这些字段）返回空串，日志格式不受影响。
        /// </para>
        /// </summary>
        private static string ExtractContextSuffix(ActionExecutingContext context)
        {
            var parts = new List<string>(2);

            if (context.RouteData.Values.TryGetValue("variableKey", out var variableKey) && variableKey != null)
            {
                parts.Add($"变量=[{variableKey}]");
            }

            foreach (var arg in context.ActionArguments.Values)
            {
                if (arg == null)
                {
                    continue;
                }

                var valueProperty = arg.GetType().GetProperty("Value");
                if (valueProperty == null)
                {
                    continue;
                }

                var value = valueProperty.GetValue(arg)?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    // 截断防止大对象写入值撑爆日志（整体内容另受 MaxContentLength 约束）
                    parts.Add($"写入值=[{(value.Length <= 100 ? value : value[..100] + "…")}]");
                }
                break;
            }

            return parts.Count > 0 ? " " + string.Join(" ", parts) : string.Empty;
        }

        private async Task TryRecordAsync(string? relatedId, string description, string level)
        {
            try
            {
                await _audit.RecordAsync(_module, _operation, relatedId, description, level, _category);
            }
            catch (Exception ex)
            {
                // 审计失败不影响主业务
                _logger.LogError(ex, "操作日志审计写入失败（module={Module}, operation={Operation}）。", _module, _operation);
            }
        }
    }
}
