using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 系统脚本实体。
    /// <para>
    /// 脚本代码（<see cref="Code"/>）只包含逻辑函数声明（run / onChange / onError），
    /// 不承载元数据；触发类型/间隔/监听/权限等元数据均为结构化字段，引擎据此调度与沙箱化执行。
    /// 代码在服务端经 Jint 沙箱执行，浏览器仅负责编辑与展示。
    /// </para>
    /// </summary>
    [Table("SystemScripts")]
    public class SystemScript
    {
        /// <summary>
        /// 主键（自增）
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 脚本名称
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 脚本代码内容（仅含逻辑函数声明）
        /// </summary>
        [Column(TypeName = "text")]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 触发类型（存储 <see cref="Enums.ScriptTriggerType"/> 枚举名字符串）。
        /// </summary>
        [MaxLength(16)]
        public string TriggerType { get; set; } = Enums.ScriptTriggerType.Manual.ToString();

        /// <summary>
        /// 执行间隔（秒），Periodic 触发时使用，必填且 ≥1。
        /// </summary>
        public int? IntervalSeconds { get; set; }

        /// <summary>
        /// Cron 表达式，Schedule 触发时使用（时区统一按 Asia/Shanghai 调度）。
        /// </summary>
        [MaxLength(100)]
        public string? CronExpression { get; set; }

        /// <summary>
        /// 监听设备键，OnChange 触发时使用（与 <see cref="WatchVariableKey"/> 成对必填）。
        /// </summary>
        [MaxLength(64)]
        public string? WatchDeviceKey { get; set; }

        /// <summary>
        /// 监听变量键，OnChange 触发时使用。
        /// </summary>
        [MaxLength(64)]
        public string? WatchVariableKey { get; set; }

        /// <summary>
        /// OnChange 死区阈值：|newValue - oldValue| > DeadBand 才触发（空 = 任意变化）。
        /// </summary>
        public double? DeadBand { get; set; }

        /// <summary>
        /// OnChange 触发冷却时间（毫秒），抑制抖动，默认 500，范围 [100, 60000]。
        /// </summary>
        public int CooldownMs { get; set; } = 500;

        /// <summary>
        /// 单次执行超时（毫秒），默认 2000，范围 [500, 30000]。
        /// </summary>
        public int TimeoutMs { get; set; } = 2000;

        /// <summary>
        /// 读授权：分号分隔的设备键列表（设备级，不含全局 *）。
        /// </summary>
        [MaxLength(1000)]
        public string? ScopeRead { get; set; }

        /// <summary>
        /// 写授权：分号分隔的 "设备键.变量键" 列表（变量级，禁止设备级通配）。
        /// </summary>
        [MaxLength(2000)]
        public string? ScopeWrite { get; set; }

        /// <summary>
        /// 是否启用（调度器仅执行已启用脚本）。
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// 脚本版本，保存时自动 +1。
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// 连续失败计数（成功清零），达到阈值触发熔断。
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// 熔断标记：连续失败达阈值时为 true，调度器跳过该脚本；人工重置后恢复。
        /// </summary>
        public bool Tripped { get; set; }

        /// <summary>
        /// 最近一次执行错误信息（熔断/告警提示用）。
        /// </summary>
        [MaxLength(2000)]
        public string? LastError { get; set; }

        /// <summary>
        /// 最近一次执行开始时间。
        /// </summary>
        public DateTime? LastExecutedAt { get; set; }

        /// <summary>
        /// 最近一次执行耗时（毫秒）。
        /// </summary>
        public int? LastDurationMs { get; set; }
    }
}