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

        public HistoryAppService(IVariableHistoryRepository repository)
        {
            _repository = repository;
        }

        /// <inheritdoc/>
        public async Task<List<HistoryRecordDto>> GetHistoryAsync(string variableKey, int limit)
        {
            if (string.IsNullOrWhiteSpace(variableKey))
            {
                return new List<HistoryRecordDto>();
            }

            if (limit <= 0) limit = 100;
            if (limit > 10000) limit = 10000;

            // 取最近 limit 条（倒序），转升序返回，便于前端按时间顺序绘制曲线。
            var records = await _repository.GetLatestAsync(variableKey.Trim(), limit);

            return records
                .OrderBy(r => r.Timestamp)
                .Select(r => new HistoryRecordDto
                {
                    Id = r.Id,
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
