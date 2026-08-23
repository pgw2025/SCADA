using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 联动规则实体（从原 VariableTrigger 拆分而来，仅承载联动语义）
    /// </summary>
    [Table("LinkageRules")]
    public class LinkageRule : EntityBase
    {
        /// <summary>
        /// 规则名称
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 关联的设备ID（触发源设备）
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 触发联动的变量键
        /// </summary>
        [MaxLength(50)]
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 触发条件（枚举，替代原 string Condition）
        /// </summary>
        public TriggerConditionEnum Condition { get; set; }

        /// <summary>
        /// 阈值
        /// </summary>
        public double Threshold { get; set; }

        /// <summary>
        /// 联动动作类型（枚举，替代原 string ActionType）
        /// </summary>
        public LinkageActionEnum ActionType { get; set; }

        /// <summary>
        /// 联动目标变量键
        /// </summary>
        [MaxLength(50)]
        public string LinkageVariableKey { get; set; } = string.Empty;

        /// <summary>
        /// 联动值（按目标变量类型解析；字符串以兼容多类型，运行时强校验）
        /// </summary>
        [MaxLength(200)]
        public string LinkageValue { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Active { get; set; } = true;
    }
}
