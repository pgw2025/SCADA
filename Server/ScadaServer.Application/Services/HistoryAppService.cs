using System.Text;
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
        /// <summary>批量查询单次变量数上限</summary>
        private const int MaxBatchVariables = 8;

        /// <summary>查询/导出行数上限</summary>
        private const int MaxLimit = 10000;

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
        public async Task<List<HistoryRecordDto>> GetHistoryAsync(
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start = null,
            DateTime? end = null,
            long? aggregateWindowMs = null,
            string aggregateFn = "mean")
        {
            if (string.IsNullOrWhiteSpace(variableKey))
            {
                return new List<HistoryRecordDto>();
            }

            return await QuerySingleAsync(deviceKey, variableKey, limit, start, end, aggregateWindowMs, aggregateFn);
        }

        /// <inheritdoc/>
        public async Task<HistoryBatchResponseDto> GetHistoryBatchAsync(HistoryBatchRequestDto request)
        {
            var response = new HistoryBatchResponseDto();
            if (request.Variables == null || request.Variables.Count == 0)
            {
                return response;
            }

            var variables = request.Variables
                .Where(v => !string.IsNullOrWhiteSpace(v.VariableKey))
                .Take(MaxBatchVariables)
                .ToList();

            foreach (var v in variables)
            {
                var records = await QuerySingleAsync(
                    v.DeviceKey, v.VariableKey, request.Limit,
                    request.Start, request.End, request.AggregateWindowMs, request.AggregateFn);

                response.Items.Add(new HistoryBatchItemDto
                {
                    DeviceKey = v.DeviceKey,
                    VariableKey = v.VariableKey,
                    VariableName = records.FirstOrDefault()?.VariableName ?? v.VariableKey,
                    Records = records
                });
            }

            return response;
        }

        /// <inheritdoc/>
        public async Task<byte[]> ExportCsvAsync(
            List<HistoryBatchVariableDto> variables,
            DateTime? start,
            DateTime? end,
            long? aggregateWindowMs,
            string aggregateFn,
            int limit)
        {
            var vars = variables?
                .Where(v => !string.IsNullOrWhiteSpace(v.VariableKey))
                .Take(MaxBatchVariables)
                .ToList() ?? new List<HistoryBatchVariableDto>();

            var sb = new StringBuilder();
            sb.Append('\ufeff'); // UTF-8 BOM：保证 Excel 直接打开中文不乱码
            sb.Append("时间(UTC),设备Key,变量Key,变量名,值,质量位\n");

            // 各变量独立查询（Influx 优先 + MySQL 回退），按时间升序逐行输出（长表格式）。
            foreach (var v in vars)
            {
                var records = await QuerySingleAsync(
                    v.DeviceKey, v.VariableKey, limit, start, end, aggregateWindowMs, aggregateFn);

                foreach (var rec in records)
                {
                    sb.Append($"\"{rec.Timestamp:O}\",");
                    sb.Append($"\"{rec.DeviceKey}\",");
                    sb.Append($"\"{rec.VariableKey}\",");
                    sb.Append($"\"{EscapeCsv(rec.VariableName)}\",");
                    sb.Append(rec.Value);
                    sb.Append($",\"{rec.Quality ?? "Good"}\"\n");
                }
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>
        /// 单变量历史查询核心：InfluxDB 优先（已配置且能返回数据），否则回退 MySQL，
        /// 保证迁移期/未配置时序库时仍可读到存量数据。
        /// </summary>
        private async Task<List<HistoryRecordDto>> QuerySingleAsync(
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start,
            DateTime? end,
            long? aggregateWindowMs,
            string aggregateFn)
        {
            if (limit <= 0) limit = 100;
            if (limit > MaxLimit) limit = MaxLimit;

            var normalizedKey = variableKey.Trim();
            var normalizedDevice = string.IsNullOrWhiteSpace(deviceKey) ? string.Empty : deviceKey.Trim();

            if (_influxStore.IsConfigured)
            {
                var influxRecords = await _influxStore.QueryLatestAsync(
                    normalizedDevice, normalizedKey, limit, start, end, aggregateWindowMs, aggregateFn);
                if (influxRecords.Count > 0)
                {
                    return influxRecords
                        .OrderBy(r => r.Timestamp)
                        .ToList();
                }
            }

            // 取最近 limit 条（倒序，按时间范围过滤），转升序返回，便于前端按时间顺序绘制曲线。
            var records = await _repository.GetLatestAsync(normalizedDevice, normalizedKey, limit, start, end);

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

        /// <summary>CSV 字段转义：包裹双引号并将字段内引号翻倍。</summary>
        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\"", "\"\"");
        }
    }
}
