using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 控制器仓储实现，对应表 Controllers，用于管理控制器/PLC 资产台账（阶段 2）。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IControllerRepository"/> 暴露给上层。
    /// </summary>
    public class ControllerRepository : RepositoryBase<Controller, int>, IControllerRepository
    {
        public ControllerRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 单条加载显式 Include Protocol（详情需展示协议名称/类型）。
        /// 基类默认不带 Include，若不加载会导致 <see cref="Controller.Protocol"/> 恒为 null，
        /// 从而 ControllerDto.ProtocolName 派生字段取不到正确值。
        /// </summary>
        public override async Task<Controller?> GetByIdAsync(int id)
        {
            return await Db.Controllers
                .AsNoTracking()
                .Include(c => c.Protocol)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        /// <summary>
        /// 更新专用加载：跟踪查询，仅加载控制器本体（不含导航）。
        /// 不使用 AsNoTracking、不 Include Protocol，使 Update(entity) 无需附加导航对象图，
        /// 避免与协议存在性校验加载的 Protocol 实体产生同主键跟踪冲突。
        /// </summary>
        public async Task<Controller?> GetByIdForUpdateAsync(int id)
        {
            return await Db.Controllers
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        /// <summary>
        /// 列表加载显式 Include Protocol（控制器卡片需展示协议名称/类型）。
        /// </summary>
        public override async Task<List<Controller>> GetListAsync()
        {
            return await Db.Controllers
                .AsNoTracking()
                .Include(c => c.Protocol)
                .ToListAsync();
        }

        /// <inheritdoc/>
        public async Task<(int Total, List<Controller> Items)> QueryAsync(
            int? protocolId,
            string? keyword,
            int pageIndex,
            int pageSize)
        {
            var query = Db.Controllers.AsNoTracking().AsQueryable();

            if (protocolId.HasValue)
                query = query.Where(c => c.ProtocolId == protocolId.Value);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                query = query.Where(c =>
                    c.Code.Contains(kw) ||
                    c.Name.Contains(kw) ||
                    (c.Manufacturer != null && c.Manufacturer.Contains(kw)) ||
                    (c.Model != null && c.Model.Contains(kw)));
            }

            var total = await query.CountAsync();

            var items = await query
                .Include(c => c.Protocol)
                .OrderBy(c => c.Id)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (total, items);
        }
    }
}
