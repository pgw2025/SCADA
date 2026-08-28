namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 脚本单次执行事件（引擎每次执行后发布，供前端控制台与状态同步）。
    /// </summary>
    public class ScriptExecutionEvent
    {
        /// <summary>脚本 ID。</summary>
        public int ScriptId { get; set; }

        /// <summary>执行时的脚本版本快照。</summary>
        public int ScriptVersion { get; set; }

        /// <summary>触发来源（Manual/Periodic/Schedule/OnChange/Test）。</summary>
        public string TriggerSource { get; set; } = string.Empty;

        /// <summary>执行结果（Success/Error/Timeout/Tripped/Skipped）。</summary>
        public string Result { get; set; } = string.Empty;

        /// <summary>执行开始时间（UTC）。</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>执行耗时（毫秒）。</summary>
        public int? DurationMs { get; set; }

        /// <summary>错误信息。</summary>
        public string? Error { get; set; }

        /// <summary>log 输出合并。</summary>
        public string? Output { get; set; }

        /// <summary>触发人（手动/试运行为用户名，自动为 null）。</summary>
        public string? ExecutedBy { get; set; }
    }
}