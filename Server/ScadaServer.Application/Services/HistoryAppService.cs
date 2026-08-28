using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 历史数据查询服务
    /// </summary>
    public class HistoryAppService : IHistoryAppService
    {
        private readonly IVariableHistoryRepository _repository;
        private readonly IInfluxStore _influxStore;

        public HistoryAppService(
            IVariableHistoryRepository repository,
            IInfluxStore influxStore)
        {
            _repository = repository;
            _influxStore = influxStore;
        }

        /// <inheritdoc/>
        public async Task<List<HistoryRecordDto>> GetHistoryAsync(string deviceKey, string variableKey, int limit)
        {
            if (string.IsNullOrWhiteSpace(variableKey))
            {
                return new List<HistoryRecordDto>();
            }

            if (limit <= 0) limit = 100;
            if (limit > 10000) limit = 10000;

            var normalizedKey = variableKey.Trim();
            var normalizedDevice = string.IsNullOrWhiteSpace(deviceKey) ? string.Empty : deviceKey.Trim();

            // 优先查 InfluxDB（已配置且能返回数据时直接使用）；
            // 无配置或无数据时回退 MySQL，保证迁移期/未配置时序库时仍可读到存量数据。
            if (_influxStore.IsConfigured)
            {
                var influxRecords = await _influxStore.QueryLatestAsync(normalizedDevice, normalizedKey, limit);
                if (influxRecords.Count > 0)
                {
                    return influxRecords
                        .OrderBy(r => r.Timestamp)
                        .ToList();
                }
            }

            // 取最近 limit 条（倒序），转升序返回，便于前端按时间顺序绘制曲线。
            var records = await _repository.GetLatestAsync(normalizedDevice, normalizedKey, limit);

            return records
                .OrderBy(r => r.Timestamp)
                .Select(r => new HistoryRecordDto
                {
                    Id = r.Id,
                    DeviceId = r.DeviceId,
                    DeviceKey = r.DeviceKey,
                    VariableKey = r.VariableKey,
                    VariableName = r.VariableName,
                    Value = r.Value,
                    RawValue = r.RawValue,
                    Timestamp = r.Timestamp,
                    Quality = r.Quality
                })
                .ToList();
        }
    }
}
