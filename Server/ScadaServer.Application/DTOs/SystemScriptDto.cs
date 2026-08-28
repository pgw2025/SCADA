using System.ComponentModel.DataAnnotations;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 系统脚本 DTO（承载结构化元数据 + 代码；代码仅含逻辑函数声明，元数据驱动调度与沙箱执行）。
    /// </summary>
    public class SystemScriptDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "脚本名称不能为空")]
        [StringLength(100, ErrorMessage = "名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "脚本代码不能为空")]
        public string Code { get; set; } = string.Empty;

        /// <summary>触发类型（Manual/Periodic/Schedule/OnChange）。</summary>
        public string TriggerType { get; set; } = ScriptTriggerType.Manual.ToString();

        /// <summary>执行间隔（秒），Periodic 触发时必填且 ≥1。</summary>
        [Range(1, 86400, ErrorMessage = "执行间隔需在 1-86400 秒之间")]
        public int? IntervalSeconds { get; set; }

        /// <summary>Cron 表达式，Schedule 触发时必填。</summary>
        [StringLength(100)]
        public string? CronExpression { get; set; }

        /// <summary>监听设备键，OnChange 触发时与 WatchVariableKey 成对必填。</summary>
        [StringLength(64)]
        public string? WatchDeviceKey { get; set; }

        /// <summary>监听变量键，OnChange 触发时必填。</summary>
        [StringLength(64)]
        public string? WatchVariableKey { get; set; }

        /// <summary>OnChange 死区阈值：|new-old| &gt; DeadBand 才触发（空 = 任意变化）。</summary>
        public double? DeadBand { get; set; }

        /// <summary>OnChange 触发冷却时间（毫秒），默认 500，范围 [100, 60000]。</summary>
        [Range(100, 60000, ErrorMessage = "冷却时间需在 100-60000 毫秒之间")]
        public int CooldownMs { get; set; } = 500;

        /// <summary>单次执行超时（毫秒），默认 2000，范围 [500, 30000]。</summary>
        [Range(500, 30000, ErrorMessage = "执行超时需在 500-30000 毫秒之间")]
        public int TimeoutMs { get; set; } = 2000;

        /// <summary>读授权：分号分隔的设备键列表（设备级）。</summary>
        [StringLength(1000)]
        public string? ScopeRead { get; set; }

        /// <summary>写授权：分号分隔的 "设备键.变量键" 列表（变量级）。</summary>
        [StringLength(2000)]
        public string? ScopeWrite { get; set; }

        /// <summary>是否启用（调度器仅执行已启用脚本，OnChange 订阅同理）。</summary>
        public bool Active { get; set; } = true;

        /// <summary>脚本版本，保存时自动 +1。</summary>
        public int Version { get; set; } = 1;

        /// <summary>连续失败计数（EngineHost 维护）。</summary>
        public int FailureCount { get; set; }

        /// <summary>熔断标记：连续失败达阈值时为 true，调度器跳过。</summary>
        public bool Tripped { get; set; }

        /// <summary>最近执行错误信息。</summary>
        [StringLength(2000)]
        public string? LastError { get; set; }

        /// <summary>最近执行开始时间（UTC）。</summary>
        public DateTime? LastExecutedAt { get; set; }

        /// <summary>最近执行耗时（毫秒）。</summary>
        public int? LastDurationMs { get; set; }
    }
}