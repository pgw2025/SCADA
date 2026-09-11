using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// SCADA 画面文件夹仓储实现，对应表 ScadaPageFolders，用于管理组态画面列表的文件夹分类。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IScadaPageFolderRepository"/> 暴露给上层。
    /// </summary>
    public class ScadaPageFolderRepository : RepositoryBase<ScadaPageFolder, int>, IScadaPageFolderRepository
    {
        public ScadaPageFolderRepository(ScadaDbContext db) : base(db)
        {
        }
    }
}