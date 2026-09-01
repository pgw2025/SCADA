using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// SCADA 工程仓储实现，对应表 ScadaProjects，用于管理组态工程的整体信息。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IScadaProjectRepository"/> 暴露给上层。
    /// </summary>
    public class ScadaProjectRepository : RepositoryBase<ScadaProject, int>, IScadaProjectRepository
    {
        public ScadaProjectRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}