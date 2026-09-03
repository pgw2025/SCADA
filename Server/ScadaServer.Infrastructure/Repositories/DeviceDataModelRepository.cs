using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 设备-数据模型绑定仓储实现（阶段 5），对应表 DeviceDataModels（多对多中间表）。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，通过 <see cref="IDeviceDataModelRepository"/> 暴露给上层。
    /// </summary>
    public class DeviceDataModelRepository : RepositoryBase<DeviceDataModel, int>, IDeviceDataModelRepository
    {
        public DeviceDataModelRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 查询某设备的全部绑定（含 <see cref="DeviceDataModel.DataModel"/> 摘要导航，AsNoTracking）。
        /// 返回顺序：主模型优先，其次按 Id 升序（展示/管理稳定序）。
        /// </summary>
        public async Task<List<DeviceDataModel>> GetByDeviceAsync(int deviceId)
        {
            return await Db.DeviceDataModels
                .AsNoTracking()
                .Include(b => b.DataModel)
                .Where(b => b.DeviceId == deviceId)
                .OrderByDescending(b => b.IsPrimary)
                .ThenBy(b => b.Id)
                .ToListAsync();
        }

        /// <summary>
        /// 批量查询多台设备的绑定（含 DataModel 摘要导航，AsNoTracking），供设备列表 N+1 优化。
        /// </summary>
        public async Task<List<DeviceDataModel>> GetByDevicesAsync(IEnumerable<int> deviceIds)
        {
            var ids = deviceIds.ToList();
            if (ids.Count == 0)
            {
                return new List<DeviceDataModel>();
            }

            return await Db.DeviceDataModels
                .AsNoTracking()
                .Include(b => b.DataModel)
                .Where(b => ids.Contains(b.DeviceId))
                .OrderByDescending(b => b.IsPrimary)
                .ThenBy(b => b.Id)
                .ToListAsync();
        }

        /// <summary>
        /// 更新专用加载（跟踪查询，不含导航）：加载某设备全部绑定行供主模型降级/切换，
        /// 使 Update(entity) 无需附加导航对象图，避免与模型/设备实体的跟踪冲突。
        /// </summary>
        public async Task<List<DeviceDataModel>> GetByDeviceForUpdateAsync(int deviceId)
        {
            return await Db.DeviceDataModels
                .Where(b => b.DeviceId == deviceId)
                .OrderByDescending(b => b.IsPrimary)
                .ThenBy(b => b.Id)
                .ToListAsync();
        }
    }
}
