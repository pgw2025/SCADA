using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 报警规则仓储实现，对应数据库表 <c>AlarmRules</c>（主键为 int）。
    /// <para>
    /// 承载报警规则的增删改查等通用数据访问操作，由基类 <see cref="RepositoryBase{TEntity, TKey}"/> 提供，
    /// 本类无自定义逻辑，仅用于定位报警规则对象并交由上层 AlarmService 处理报警语义。
    /// </para>
    /// </summary>
    public class AlarmRuleRepository : RepositoryBase<AlarmRule, int>, IAlarmRuleRepository
    {
        public AlarmRuleRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}