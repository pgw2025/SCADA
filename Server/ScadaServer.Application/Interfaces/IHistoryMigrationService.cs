using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 历史数据迁移服务。
    /// <para>
    /// 将 MySQL <c>VariableHistory</c> 表中的存量历史数据迁移写入当前生效的 InfluxDB 历史库，
    /// 供趋势查询在切换双库架构后仍能读取旧记录。迁移为管理员手动触发，支持断点续传、进度查询与取消。
    /// </para>
    /// </summary>
    public interface IHistoryMigrationService
    {
        /// <summary>
        /// 触发一次历史数据迁移（读 MySQL 存量 → 写 InfluxDB），立即返回启动结果。
        /// <para>若已有迁移任务在运行则返回 started=false；迁移前会将 InfluxStore 重建到生效历史库配置；
        /// 若存在上次中断断点，则从断点续传。</para>
        /// </summary>
        Task<HistoryMigrationResult> MigrateAsync();

        /// <summary>查询当前迁移任务状态（无任务返回 NeverStarted + 已持久化的断点信息）。</summary>
        Task<HistoryMigrationResult> GetStatusAsync();

        /// <summary>请求取消当前迁移任务（下一片边界生效，幂等）。</summary>
        Task<HistoryMigrationResult> CancelAsync();

        /// <summary>当前是否有迁移任务在运行（内存态，供启动恢复逻辑判断僵尸状态）。</summary>
        bool IsRunning();
    }
}
