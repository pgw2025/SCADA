using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 联动规则仓储实现。
    /// 对应实体 <see cref="LinkageRule"/>，数据表 LinkageRules（设备变量触发联动的规则：
    /// 由原始 VariableTrigger 拆分而来，仅承载联动语义，含触发源、条件阈值、动作类型及目标变量键等）。
    /// 直接复用基类 <see cref="RepositoryBase{TEntity,TKey}"/> 的通用增删改查，无自定义查询逻辑。
    /// </summary>
    public class LinkageRuleRepository : RepositoryBase<LinkageRule, int>, ILinkageRuleRepository
    {
        /// <summary>
        /// 初始化联动规则仓储。
        /// </summary>
        /// <param name="db">共享的 EF Core 数据库上下文（由依赖注入提供）。</param>
        public LinkageRuleRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}
