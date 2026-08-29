namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 定时任务类型常量（存储于 ScheduledTask.Type 字符串字段）。
    /// </summary>
    public static class ScheduledTaskTypes
    {
        /// <summary>变量写入：向指定设备的变量写入固定值</summary>
        public const string SetValue = "set_value";

        /// <summary>数据备份：导出 MySQL 业务配置 + InfluxDB 时序历史到备份文件</summary>
        public const string Backup = "backup";

        /// <summary>脚本执行：调用系统脚本（Jint 沙箱）执行</summary>
        public const string ExecuteScript = "execute_script";

        /// <summary>历史清理：删除 InfluxDB 中超过保留期的时序数据</summary>
        public const string ClearHistory = "clear_history";

        /// <summary>全部合法任务类型（校验白名单）</summary>
        public static readonly string[] All = { SetValue, Backup, ExecuteScript, ClearHistory };
    }
}
