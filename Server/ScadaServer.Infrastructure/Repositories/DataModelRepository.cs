using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 数据模型仓储实现，对应数据库表 <c>DataModels</c>（主键为 int）。
    /// <para>
    /// 承载"数据模型"（描述设备型号，并通过 <c>ProtocolId</c> 关联通信协议确定驱动方式）的通用增删改查数据访问操作。
    /// 查询时须显式加载导航属性 <see cref="DataModel.Protocol"/>（<c>DataModel.Variables</c> 为 [NotMapped]，EF 不会加载），
    /// 因此重写了基类查询方法以附加 Include。
    /// </para>
    /// </summary>
    public class DataModelRepository : RepositoryBase<DataModel, int>, IDataModelRepository
    {
        public DataModelRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 显式加载 <see cref="DataModel.Protocol"/>（协议真相源）。基类默认不带 Include，若不加载则
        /// <see cref="DataModel.Protocol"/> 恒为 null，无法取到协议信息（<see cref="DataModel.ProtocolId"/> 仅存外键）。
        /// 注意：<see cref="DataModel.Variables"/> 为 [NotMapped]，EF 不会加载，变量需由 AppService 显式查询。
        /// </summary>
        /// <param name="id">数据模型主键。</param>
        /// <returns>指定 ID 的数据模型（含 Protocol 导航属性）；不存在时返回 null。</returns>
        public override async Task<DataModel?> GetByIdAsync(int id)
        {
            return await Db.DataModels
                .AsNoTracking()
                .Include(m => m.Protocol)
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        /// <summary>
        /// 更新/删除专用加载（跟踪查询、不含导航）：仅加载数据模型本体。
        /// 不使用 AsNoTracking、不 Include Protocol，使 Update(entity)/Remove(entity) 无需附加
        /// 导航对象图，避免与协议存在性校验加载的 Protocol 实体产生同主键跟踪冲突
        /// （与 Controller/Device/DeviceConnection 仓储的 GetByIdForUpdateAsync 模式一致）。
        /// </summary>
        public async Task<DataModel?> GetByIdForUpdateAsync(int id)
        {
            return await Db.DataModels
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        /// <summary>
        /// 显式加载与 <see cref="GetByIdAsync"/> 一致的导航属性（<c>Protocol</c>）。
        /// </summary>
        /// <returns>全部数据模型列表（每项均含 Protocol 导航属性）。</returns>
        public override async Task<List<DataModel>> GetListAsync()
        {
            return await Db.DataModels
                .AsNoTracking()
                .Include(m => m.Protocol)
                .ToListAsync();
        }
    }
}