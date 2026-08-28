using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// 历史数据迁移服务。
    /// <para>
    /// 将 MySQL <c>VariableHistory</c> 表中的存量历史数据一次性迁移写入当前生效的 InfluxDB 历史库，
    /// 供趋势查询在切换双库架构后仍能读取旧记录。迁移为管理员手动触发，前后台自持并发锁防止重复执行。
    /// </para>
    /// </summary>
    public interface IHistoryMigrationService
    {
        /// <summary>
        /// 触发一次历史数据迁移（读 MySQL 存量 → 写 InfluxDB）。
        /// 若已有迁移任务在运行则直接返回当前状态；迁移前会将 InfluxStore 重建到生效历史库配置。
        /// </summary>
        Task<HistoryMigrationResult> MigrateAsync();

        /// <summary>当前是否有迁移任务在运行</summary>
        bool IsRunning();
    }
}