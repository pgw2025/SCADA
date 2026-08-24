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
        /// 显式加载导航属性：Area、Config（一对一配置）、Model→Protocol（协议真相源）。
        /// 基类默认不带 Include，若不加会导致 <see cref="Device.Model"/>/<see cref="Device.Config"/> 恒为 null，
        /// 从而 DeviceDto.ModelType/ProtocolKey 等派生字段取不到正确值。
        /// </summary>
        public override async Task<Device?> GetByIdAsync(int id)
        {
            return await Db.Devices
                .AsNoTracking()
                .Include(d => d.Area)
                .Include(d => d.Config)
                .Include(d => d.Model)
                    .ThenInclude(m => m!.Protocol)
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
                .Include(d => d.Config)
                .Include(d => d.Model)
                    .ThenInclude(m => m!.Protocol)
                .ToListAsync();
        }
    }
}