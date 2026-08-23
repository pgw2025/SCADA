using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class LinkageRuleRepository : RepositoryBase<LinkageRule, int>, ILinkageRuleRepository
    {
        public LinkageRuleRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}
