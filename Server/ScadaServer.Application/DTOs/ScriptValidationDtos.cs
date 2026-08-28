namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 脚本校验单条问题（错误阻止保存，警告仅提示）。
    /// </summary>
    public class ScriptValidationIssue
    {
        /// <summary>级别：Error（阻止）/ Warning（提示）。</summary>
        public string Level { get; set; } = "Error";

        /// <summary>问题描述。</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 脚本校验结果。
    /// </summary>
    public class ScriptValidationResult
    {
        /// <summary>是否通过（无 Error 级问题即视为通过）。</summary>
        public bool Valid { get; set; }

        /// <summary>问题列表（Error + Warning）。</summary>
        public List<ScriptValidationIssue> Issues { get; set; } = new();
    }
}