using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// SCADA 画面（页面）仓储实现，对应表 ScadaPages，用于管理组态中的 HMI 画面及其层级关系。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IScadaPageRepository"/> 暴露给上层。
    /// </summary>
    public class ScadaPageRepository : RepositoryBase<ScadaPage, int>, IScadaPageRepository
    {
        public ScadaPageRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}