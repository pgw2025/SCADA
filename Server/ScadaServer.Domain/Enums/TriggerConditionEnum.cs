namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 触发器条件（替代原 string Condition）
    /// </summary>
    public enum TriggerConditionEnum
    {
        /// <summary>大于</summary>
        GreaterThan,
        /// <summary>大于等于</summary>
        GreaterOrEqual,
        /// <summary>小于</summary>
        LessThan,
        /// <summary>小于等于</summary>
        LessOrEqual,
        /// <summary>等于</summary>
        EqualTo,
        /// <summary>不等于</summary>
        NotEqualTo
    }
}
