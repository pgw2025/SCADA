using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 数据模型仓储实现，对应数据库表 <c>DataModels</c>（主键为 int）。
    /// <para>
    /// 承载"数据模型"（描述设备型号）的通用增删改查数据访问操作。
    /// 协议由设备所附连接决定，数据模型实体不再绑定协议（<c>DataModel.Variables</c> 为 [NotMapped]，EF 不会加载）。
    /// </para>
    /// </summary>
    public class DataModelRepository : RepositoryBase<DataModel, int>, IDataModelRepository
    {
        public DataModelRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 按主键加载数据模型本体（<c>DataModel.Variables</c> 为 [NotMapped]，EF 不会加载，变量需由 AppService 显式查询）。
        /// </summary>
        /// <param name="id">数据模型主键。</param>
        /// <returns>指定 ID 的数据模型；不存在时返回 null。</returns>
        public override async Task<DataModel?> GetByIdAsync(int id)
        {
            return await Db.DataModels
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        /// <summary>
        /// 更新/删除专用加载（跟踪查询）：仅加载数据模型本体。
        /// </summary>
        public async Task<DataModel?> GetByIdForUpdateAsync(int id)
        {
            return await Db.DataModels
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        /// <summary>
        /// 返回全部数据模型列表（均为数据模型本体）。
        /// </summary>
        /// <returns>全部数据模型列表。</returns>
        public override async Task<List<DataModel>> GetListAsync()
        {
            return await Db.DataModels
                .AsNoTracking()
                .ToListAsync();
        }
    }
}