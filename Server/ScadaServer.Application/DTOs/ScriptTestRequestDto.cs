namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 脚本试运行请求：携带待测脚本（不落库）+ 可选的变量上下文（OnChange 测试时传入，用于构造 onChange 事件的 value/quality）。
    /// </summary>
    public class ScriptTestRequestDto
    {
        /// <summary>待测脚本内容与元数据（不落库）。</summary>
        public SystemScriptDto Script { get; set; } = new();

        /// <summary>变量上下文设备键（可选，OnChange 测试用）。</summary>
        public string? DeviceKey { get; set; }

        /// <summary>变量上下文变量键（可选，OnChange 测试用）。</summary>
        public string? VariableKey { get; set; }
    }
}