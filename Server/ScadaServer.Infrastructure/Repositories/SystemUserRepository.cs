using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 系统用户仓储实现，对应表 SystemUsers，用于管理登录用户及其账户信息、凭据。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="ISystemUserRepository"/> 暴露给上层。
    /// </summary>
    public class SystemUserRepository : RepositoryBase<SystemUser, int>, ISystemUserRepository
    {
        public SystemUserRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}