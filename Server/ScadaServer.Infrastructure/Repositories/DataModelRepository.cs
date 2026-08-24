using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DataModelRepository : RepositoryBase<DataModel, int>, IDataModelRepository
    {
        public DataModelRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 显式加载 Protocol（协议真相源）。基类默认不带 Include，若不加载则
        /// <see cref="DataModel.Protocol"/> 恒为 null，协议字段（ProtocolKey/ProtocolName）取不到值。
        /// 注意：<see cref="DataModel.Variables"/> 为 [NotMapped]，EF 不会加载，变量需由 AppService 显式查询。
        /// </summary>
        public override async Task<DataModel?> GetByIdAsync(int id)
        {
            return await Db.DataModels
                .AsNoTracking()
                .Include(m => m.Protocol)
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        /// <summary>
        /// 显式加载与 <see cref="GetByIdAsync"/> 一致的导航属性。
        /// </summary>
        public override async Task<List<DataModel>> GetListAsync()
        {
            return await Db.DataModels
                .AsNoTracking()
                .Include(m => m.Protocol)
                .ToListAsync();
        }
    }
}