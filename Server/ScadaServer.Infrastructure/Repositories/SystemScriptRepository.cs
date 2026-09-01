using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 系统脚本仓储实现，对应表 SystemScripts，用于管理系统中可执行的脚本定义。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="ISystemScriptRepository"/> 暴露给上层。
    /// </summary>
    public class SystemScriptRepository : RepositoryBase<SystemScript, int>, ISystemScriptRepository
    {
        public SystemScriptRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}