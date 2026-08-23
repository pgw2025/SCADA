using SqlSugar;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class AlarmRuleRepository : RepositoryBase<AlarmRule, int>, IAlarmRuleRepository
    {
        public AlarmRuleRepository(ISqlSugarClient db) : base(db)
        {
        }
    }
}