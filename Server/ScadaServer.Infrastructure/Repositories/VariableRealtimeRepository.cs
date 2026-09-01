using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 变量实时快照仓储实现（对应表 VariableRealtime，复合主键 DeviceId+VariableKey）。
    /// 不继承 RepositoryBase，而是直接持有 <see cref="ScadaDbContext"/> 注入，仅提供实时快照的只读查询。
    /// </summary>
    public class VariableRealtimeRepository : IVariableRealtimeRepository
    {
        private readonly ScadaDbContext _db;

        public VariableRealtimeRepository(ScadaDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// 依据设备 ID 与变量标识精确查询单条实时快照。
        /// </summary>
        /// <param name="deviceId">设备 ID。</param>
        /// <param name="variableKey">变量标识。</param>
        /// <returns>匹配的实时快照；未找到时返回 null。</returns>
        public Task<VariableRealtime?> GetByDeviceAndKeyAsync(int deviceId, string variableKey)
        {
            return _db.VariableRealtimes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.DeviceId == deviceId && r.VariableKey == variableKey);
        }

        /// <summary>
        /// 依据设备标识查询该设备的全部实时快照。
        /// </summary>
        /// <param name="deviceKey">设备标识；为空白字符串时直接返回空列表（防御性处理）。</param>
        /// <returns>该设备名下所有实时快照列表。</returns>
        public async Task<List<VariableRealtime>> GetAllByDeviceAsync(string deviceKey)
        {
            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                return new List<VariableRealtime>();
            }

            return await _db.VariableRealtimes
                .AsNoTracking()
                .Where(r => r.DeviceKey == deviceKey)
                .ToListAsync();
        }
    }
}