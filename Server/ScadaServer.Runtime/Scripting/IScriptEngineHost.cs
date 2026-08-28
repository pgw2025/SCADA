using ScadaServer.Domain.Entities;

namespace ScadaServer.Runtime.Scripting
{
    /// <summary>
    /// 脚本引擎宿主：负责脚本加载、调度（周期/Cron）、变量变化订阅、沙箱执行、熔断与执行记录落库。
    /// 生命周期为 Singleton，由宿主启动时 StartAsync，CRUD 后 ReloadAsync 重载生效配置。
    /// </summary>
    public interface IScriptEngineHost
    {
        /// <summary>
        /// 从数据库重载脚本调度与订阅（新增/编辑/删除/启停后调用）。
        /// </summary>
        Task ReloadAsync();

        /// <summary>
        /// 手动执行脚本（admin 操作，绕过熔断状态）。自动记录执行日志。
        /// </summary>
        Task<ScriptEngineResult> RunAsync(int scriptId, string executedBy);

        /// <summary>
        /// 试运行（dry-run）：不写真实变量、不更新熔断状态、不落库执行记录，仅返回输出供前端调试。
        /// </summary>
        Task<ScriptEngineResult> TestAsync(SystemScript script, string? deviceContextKey, string? variableContextKey, string executedBy);
    }

    /// <summary>
    /// 脚本执行结果（返回给调用方/前端）。
    /// </summary>
    public class ScriptEngineResult
    {
        public int ScriptId { get; set; }
        public int ScriptVersion { get; set; }
        public string Result { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string? Error { get; set; }
        public int? DurationMs { get; set; }
        public bool WroteLog { get; set; }
    }
}