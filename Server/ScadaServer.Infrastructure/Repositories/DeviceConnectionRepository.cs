using Microsoft.EntityFrameworkCore;
using ScadaServer.Infrastructure.Persistence;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 设备连接仓储实现，对应表 DeviceConnections（阶段 3：连接参数抽取实体）。
    /// 继承自 <see cref="RepositoryBase{TEntity,TKey}"/>，并通过 <see cref="IDeviceConnectionRepository"/> 暴露给上层。
    /// </summary>
    public class DeviceConnectionRepository : RepositoryBase<DeviceConnection, int>, IDeviceConnectionRepository
    {
        public DeviceConnectionRepository(ScadaDbContext db) : base(db)
        {
        }

        /// <summary>
        /// 单条加载显式 Include Controller/Protocol（详情需展示控制器与协议信息）。
        /// 基类默认不带 Include，若不加载会导致 <see cref="DeviceConnection.Protocol"/> 恒为 null，
        /// 从而无法取到协议/驱动的派发信息。
        /// </summary>
        public override async Task<DeviceConnection?> GetByIdAsync(int id)
        {
            return await Db.DeviceConnections
                .AsNoTracking()
                .Include(c => c.Controller)
                .Include(c => c.Protocol)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        /// <summary>
        /// 更新专用加载：跟踪查询，仅加载连接本体（不含导航）。
        /// 不使用 AsNoTracking、不 Include Protocol，使 Update(entity) 无需附加导航对象图，
        /// 避免与协议存在性校验加载的 Protocol 实体产生同主键跟踪冲突。
        /// </summary>
        public async Task<DeviceConnection?> GetByIdForUpdateAsync(int id)
        {
            return await Db.DeviceConnections
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        /// <summary>
        /// 列表加载显式 Include Controller/Protocol。
        /// </summary>
        public override async Task<List<DeviceConnection>> GetListAsync()
        {
            return await Db.DeviceConnections
                .AsNoTracking()
                .Include(c => c.Controller)
                .Include(c => c.Protocol)
                .ToListAsync();
        }

        /// <summary>
        /// 统计每个控制器下的连接数：单条 GroupBy 查询一次取全量分布，
        /// 供控制器列表/详情展示 ConnectionCount（避免每台控制器一次 Count 查询）。
        /// </summary>
        public async Task<Dictionary<int, int>> GetCountsByControllerAsync()
        {
            return await Db.DeviceConnections
                .AsNoTracking()
                .GroupBy(c => c.ControllerId)
                .Select(g => new { ControllerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ControllerId, x => x.Count);
        }
    }
}
