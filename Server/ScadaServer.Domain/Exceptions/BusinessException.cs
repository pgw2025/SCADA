namespace ScadaServer.Domain.Exceptions
{
    /// <summary>
    /// 业务异常，用于处理应用程序的业务逻辑错误
    /// </summary>
    public class BusinessException : Exception
    {
        /// <summary>
        /// HTTP状态码
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// 错误详情
        /// </summary>
        public object? Errors { get; }

        /// <summary>
        /// 初始化业务异常
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="statusCode">HTTP状态码，默认400</param>
        /// <param name="errors">错误详情对象</param>
        public BusinessException(string message, int statusCode = 400, object? errors = null)
            : base(message)
        {
            StatusCode = statusCode;
            Errors = errors;
        }
    }
}
