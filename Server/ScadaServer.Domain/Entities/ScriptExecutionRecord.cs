using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 脚本单次执行记录（每次执行一行，用于审计与前端控制台追溯）。
    /// 由脚本引擎在每次执行后落库；保留策略由清理服务按保留天数分批删除。
    /// </summary>
    [Table("ScriptExecutionRecords")]
    public class ScriptExecutionRecord
    {
        /// <summary>
        /// 主键（自增，数据量累积）
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// 所属脚本 ID。
        /// </summary>
        public int ScriptId { get; set; }

        /// <summary>
        /// 执行时的脚本版本快照。
        /// </summary>
        public int ScriptVersion { get; set; }

        /// <summary>
        /// 触发来源（Manual / Periodic / Schedule / OnChange / Test）。
        /// </summary>
        [MaxLength(16)]
        public string TriggerSource { get; set; } = string.Empty;

        /// <summary>
        /// 执行结果（Success / Error / Timeout / Tripped / Skipped）。
        /// </summary>
        [MaxLength(16)]
        public string Result { get; set; } = string.Empty;

        /// <summary>
        /// 执行开始时间（UTC）。
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 执行耗时（毫秒）。
        /// </summary>
        public int? DurationMs { get; set; }

        /// <summary>
        /// 错误信息（截断至 2000 字符）。
        /// </summary>
        [MaxLength(4000)]
        public string? Error { get; set; }

        /// <summary>
        /// 执行期 log 输出合并（截断至 8000 字符）。
        /// </summary>
        [MaxLength(8000)]
        public string? Output { get; set; }

        /// <summary>
        /// 触发人（Manual / Test 时为用户名；自动触发为 null）。
        /// </summary>
        [MaxLength(64)]
        public string? ExecutedBy { get; set; }
    }
}