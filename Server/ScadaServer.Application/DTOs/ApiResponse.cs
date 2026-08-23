namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 统一 API 响应结构
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
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
