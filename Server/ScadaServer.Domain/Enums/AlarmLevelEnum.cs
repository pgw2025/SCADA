namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 报警级别（替代原 string AlarmLevel / Severity）
    /// </summary>
    public enum AlarmLevelEnum
    {
        /// <summary>低</summary>
        Low,
        /// <summary>中</summary>
        Medium,
        /// <summary>高</summary>
        High,
        /// <summary>紧急</summary>
        Critical
    }
}
