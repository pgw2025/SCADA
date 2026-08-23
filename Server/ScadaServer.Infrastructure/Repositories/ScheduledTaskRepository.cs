using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class ScheduledTaskRepository : RepositoryBase<ScheduledTask, int>, IScheduledTaskRepository
    {
        public ScheduledTaskRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}