namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 系统脚本触发类型。
    /// <para>
    /// <see cref="Manual"/> 手动触发；<see cref="Periodic"/> 按固定间隔周期执行；
    /// <see cref="Schedule"/> 按 Cron 表达式定时执行；<see cref="OnChange"/> 由变量变化事件触发。
    /// </para>
    /// </summary>
    public enum ScriptTriggerType
    {
        /// <summary>手动触发（调用 run 钩子）</summary>
        Manual,

        /// <summary>周期执行（IntervalSeconds，调用 run 钩子）</summary>
        Periodic,

        /// <summary>Cron 定时执行（CronExpression，调用 run 钩子）</summary>
        Schedule,

        /// <summary>变量变化事件触发（WatchDeviceKey+WatchVariableKey，调用 onChange 钩子）</summary>
        OnChange
    }
}