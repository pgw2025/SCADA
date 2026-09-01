namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 统一 API 响应结构
    /// </summary>
    /// <typeparam name="T">响应数据的类型</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>是否操作成功</summary>
        public bool Success { get; set; }

        /// <summary>返回提示信息（成功或失败消息）</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>业务数据负载（成功时有值，失败可为空）</summary>
        public T? Data { get; set; }

        /// <summary>错误详情（仅失败时使用，如字段级校验错误）</summary>
        public object? Errors { get; set; }

        public static ApiResponse<T> Ok(T? data, string message = "操作成功")
        {
            return new ApiResponse<T> { Success = true, Message = message, Data = data };
        }

        public static ApiResponse<T> Fail(string message, object? errors = null)
        {
            return new ApiResponse<T> { Success = false, Message = message, Errors = errors };
        }
    }

    /// <summary>
    /// 非泛型 API 响应结构 (用于无返回数据的场景)
    /// </summary>
    public class ApiResponse : ApiResponse<object>
    {
        public static ApiResponse Ok(string message = "操作成功")
        {
            return new ApiResponse { Success = true, Message = message };
        }

        public new static ApiResponse Fail(string message, object? errors = null)
        {
            return new ApiResponse { Success = false, Message = message, Errors = errors };
        }
    }
}
