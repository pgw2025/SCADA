using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    public class DeviceRepository : RepositoryBase<Device, int>, IDeviceRepository
    {
        public DeviceRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 显式加载导航属性：Area、Model→Protocol、Controller、Connection→Protocol。
        /// 基类默认不带 Include，若不加会导致 <see cref="Device.Model"/> 恒为 null，
        /// 从而 DeviceDto.ModelType/ProtocolKey 等派生字段取不到正确值。
        /// 协议配置（JsonConfig）已内联于 Device 行，无需再 Include。
        /// Controller/Connection 为阶段 3 新增过渡导航（连接参数抽取），详情接口需展示控制器与连接信息。
        /// DeviceDataModels（阶段 5 多对多绑定）用于 DeviceDto.Models 摘要映射。
        /// </summary>
        public override async Task<Device?> GetByIdAsync(int id)
        {
            return await Db.Devices
                .AsNoTracking()
                .Include(d => d.Area)
                .Include(d => d.Model)
                    .ThenInclude(m => m!.Protocol)
                .Include(d => d.Controller)
                .Include(d => d.Connection)
                    .ThenInclude(c => c!.Protocol)
                .Include(d => d.Connection)
                    .ThenInclude(c => c!.Controller)
                .Include(d => d.DeviceDataModels)
                    .ThenInclude(b => b!.DataModel)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        /// <summary>
        /// 更新专用加载：跟踪查询，仅加载设备本体（配置已内联为 Device 列）。
        /// 不使用 AsNoTracking、不 Include Area/Model/Controller/Connection，
        /// 使 Update(entity) 无需附加导航对象图，避免与其他跟踪实例（如区域校验加载的 Area）产生同主键跟踪冲突。
        /// </summary>
        public async Task<Device?> GetByIdForUpdateAsync(int id)
        {
            return await Db.Devices
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        /// <summary>
        /// 显式加载与 <see cref="GetByIdAsync"/> 一致的导航属性。
        /// </summary>
        public override async Task<List<Device>> GetListAsync()
        {
            return await Db.Devices
                .AsNoTracking()
                .Include(d => d.Area)
                .Include(d => d.Model)
                    .ThenInclude(m => m!.Protocol)
                .Include(d => d.Controller)
                .Include(d => d.Connection)
                    .ThenInclude(c => c!.Protocol)
                .Include(d => d.Connection)
                    .ThenInclude(c => c!.Controller)
                .Include(d => d.DeviceDataModels)
                    .ThenInclude(b => b!.DataModel)
                .ToListAsync();
        }

        /// <inheritdoc/>
        public async Task<Dictionary<int, int>> GetCountByAreaAsync()
        {
            return await Db.Devices
                .GroupBy(d => d.AreaId)
                .Select(g => new { AreaId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AreaId, x => x.Count);
        }
    }
}