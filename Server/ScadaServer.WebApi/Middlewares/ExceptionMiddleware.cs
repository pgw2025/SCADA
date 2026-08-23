using System.Net;
using System.Text.Json;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Application.DTOs;

namespace ScadaServer.WebApi.Middlewares
{
    /// <summary>
    /// 全局异常处理中间件
    /// </summary>
    /// <remarks>
    /// 捕获所有未处理的异常，统一返回格式化的错误响应。
    /// BusinessException 会返回其指定的 StatusCode，其他异常返回 500。
    /// </remarks>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _env;

        /// <summary>
        /// 初始化异常中间件
        /// </summary>
        /// <param name="next">下一个中间件</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="env">主机环境</param>
        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        /// <summary>
        /// 处理HTTP请求，捕获异常
        /// </summary>
        /// <param name="context">HTTP上下文</param>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        /// <summary>
        /// 处理异常并返回JSON响应
        /// </summary>
        /// <param name="context">HTTP上下文</param>
        /// <param name="exception">异常对象</param>
        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            var statusCode = (int)HttpStatusCode.InternalServerError;
            var message = "服务器内部错误";
            object? errors = null;

            if (exception is BusinessException bizEx)
            {
                statusCode = bizEx.StatusCode;
                message = bizEx.Message;
                errors = bizEx.Errors;
            }
            else
            {
                _logger.LogError(exception, "系统未处理异常");
                if (_env.IsDevelopment())
                {
                    message = exception.Message;
                    errors = exception.StackTrace;
                }
            }

            context.Response.StatusCode = statusCode;

            var response = ApiResponse.Fail(message, errors);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
        }
    }
}
