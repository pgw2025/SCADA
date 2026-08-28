using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Repositories
{
    /// <summary>
    /// 变量实时快照仓储（MySQL 实时库，复合主键 DeviceId+VariableKey）。
    /// </summary>
    public class VariableRealtimeRepository : IVariableRealtimeRepository
    {
        private readonly ScadaDbContext _db;

        public VariableRealtimeRepository(ScadaDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc/>
        public Task<VariableRealtime?> GetByDeviceAndKeyAsync(int deviceId, string variableKey)
        {
            return _db.VariableRealtimes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.DeviceId == deviceId && r.VariableKey == variableKey);
        }

        /// <inheritdoc/>
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