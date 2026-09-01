using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 定时任务仓储实现，对应表 ScheduledTasks，用于管理计划任务/定时作业的定义与调度配置。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IScheduledTaskRepository"/> 暴露给上层。
    /// </summary>
    public class ScheduledTaskRepository : RepositoryBase<ScheduledTask, int>, IScheduledTaskRepository
    {
        public ScheduledTaskRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}