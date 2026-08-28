namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 脚本单次执行结果。
    /// </summary>
    public enum ScriptExecutionResult
    {
        /// <summary>执行成功</summary>
        Success,

        /// <summary>脚本抛错（进入 onError 后计失败）</summary>
        Error,

        /// <summary>超出超时被中断</summary>
        Timeout,

        /// <summary>脚本处于熔断状态，本次未执行</summary>
        Tripped,

        /// <summary>到点时仍有上一轮未结束，本次被跳过（不排队）</summary>
        Skipped
    }

    /// <summary>
    /// 脚本触发来源（区分自动调度 / 手动 / 试运行）。
    /// </summary>
    public enum ScriptTriggerSource
    {
        /// <summary>手动触发</summary>
        Manual,

        /// <summary>周期调度触发</summary>
        Periodic,

        /// <summary>Cron 调度触发</summary>
        Schedule,

        /// <summary>变量变化事件触发</summary>
        OnChange,

        /// <summary>试运行（test 接口，dry-run，不熔断不真实写入）</summary>
        Test
    }
}