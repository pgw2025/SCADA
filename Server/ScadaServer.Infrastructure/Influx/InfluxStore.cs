using System.Diagnostics;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;

namespace ScadaServer.Infrastructure.Influx
{
    /// <summary>
    /// InfluxDB 时序库访问实现（单例）。
    /// <para>
    /// 行协议：measurement=variable_history，tags=device_key+variable_key（series 身份 = 设备+变量二元组），
    /// 其余（value/raw_value/quality/device_id/variable_name）作为 fields，避免 series 基数失控。
    /// </para>
    /// </summary>
    public class InfluxStore : IInfluxStore, IDisposable
    {
        public const string MeasurementName = "variable_history";

        private const int MaxWriteRetry = 3;

        private readonly ILogger<InfluxStore> _logger;
        private readonly object _lock = new();

        private InfluxDBClient? _client;
        private string _bucket = string.Empty;
        private string _org = string.Empty;

        public InfluxStore(ILogger<InfluxStore> logger)
        {
            _logger = logger;
        }

        public bool IsConfigured
        {
            get
            {
                lock (_lock)
                {
                    return _client != null && !string.IsNullOrEmpty(_bucket);
                }
            }
        }

        /// <inheritdoc/>
        public void Rebuild(DatabaseConfig config)
        {
            if (config == null)
            {
                return;
            }

            var url = BuildUrl(config);
            var token = config.Token ?? string.Empty;

            InfluxDBClient? newClient = null;
            try
            {
                newClient = new InfluxDBClient(url, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 InfluxDB 客户端失败（Url={Url}）。", url);
                return;
            }

            lock (_lock)
            {
                _client?.Dispose();
                _client = newClient;
                _bucket = !string.IsNullOrWhiteSpace(config.Bucket) ? config.Bucket : config.DatabaseName;
                _org = config.Org ?? string.Empty;
            }

            _logger.LogInformation(
                "InfluxDB 历史库配置已生效：Url={Url}, Bucket={Bucket}, Org={Org}。",
                url, _bucket, _org);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteAsync(List<VariableHistory> points)
        {
            if (points == null || points.Count == 0)
            {
                return true;
            }

            InfluxDBClient? client;
            string bucket, org;
            lock (_lock)
            {
                client = _client;
                bucket = _bucket;
                org = _org;
            }

            if (client == null || string.IsNullOrEmpty(bucket))
            {
                return false;
            }

            var data = new List<PointData>(points.Count);
            foreach (var p in points)
            {
                var point = PointData.Measurement(MeasurementName)
                    .Tag("device_key", p.DeviceKey)
                    .Tag("variable_key", p.VariableKey)
                    .Field("value", p.Value)
                    .Field("device_id", p.DeviceId);

                if (!string.IsNullOrEmpty(p.RawValue))
                {
                    point = point.Field("raw_value", p.RawValue);
                }
                if (!string.IsNullOrEmpty(p.Quality))
                {
                    point = point.Field("quality", p.Quality);
                }
                if (!string.IsNullOrEmpty(p.VariableName))
                {
                    point = point.Field("variable_name", p.VariableName);
                }

                // InfluxDB 时间戳需为 UTC（Ns 精度要求 Kind=Utc）
                point = point.Timestamp(p.Timestamp.ToUniversalTime(), WritePrecision.Ns);
                data.Add(point);
            }

            var writeApi = client.GetWriteApiAsync();
            for (var attempt = 1; attempt <= MaxWriteRetry; attempt++)
            {
                try
                {
                    await writeApi.WritePointsAsync(data, bucket, org);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "InfluxDB 批量写入失败（第 {Attempt}/{MaxRetry} 次，共 {Count} 点）。",
                        attempt, MaxWriteRetry, data.Count);
                    if (attempt < MaxWriteRetry)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt));
                    }
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public Task<List<HistoryRecordDto>> QueryLatestAsync(
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start = null,
            DateTime? end = null,
            long? aggregateWindowMs = null,
            string aggregateFn = "mean")
        {
            if (limit <= 0)
            {
                limit = 100;
            }
            if (limit > 10000)
            {
                limit = 10000;
            }

            InfluxDBClient? client;
            string bucket, org;
            lock (_lock)
            {
                client = _client;
                bucket = _bucket;
                org = _org;
            }

            var result = new List<HistoryRecordDto>();
            if (client == null || string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(org))
            {
                return Task.FromResult(result);
            }

            try
            {
                var query = BuildQuery(bucket, deviceKey, variableKey, limit, start, end, aggregateWindowMs, aggregateFn);
                var tables = client.GetQueryApiSync().QuerySync(query, org);

                foreach (var table in tables)
                {
                    foreach (var record in table.Records)
                    {
                        var time = record.GetTimeInDateTime();
                        var timestamp = time ?? DateTime.MinValue;

                        result.Add(new HistoryRecordDto
                        {
                            Id = 0,
                            VariableKey = variableKey,
                            VariableName = record.GetValueByKey("variable_name") as string ?? variableKey,
                            Value = ToDouble(record.GetValueByKey("value")),
                            RawValue = record.GetValueByKey("raw_value") as string,
                            Timestamp = timestamp,
                            Quality = record.GetValueByKey("quality") as string
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InfluxDB 历史查询失败：device={DeviceKey}, variable={VariableKey}。",
                    deviceKey, variableKey);
            }

            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public async Task<(bool Success, long LatencyMs, string Message)> PingAsync()
        {
            InfluxDBClient? client;
            lock (_lock)
            {
                client = _client;
            }

            if (client == null)
            {
                return (false, 0, "尚未配置 InfluxDB 连接。");
            }

            var sw = Stopwatch.StartNew();
            try
            {
                await client.PingAsync();
                sw.Stop();
                return (true, sw.ElapsedMilliseconds, "InfluxDB 连接正常。");
            }
            catch (Exception ex)
            {
                sw.Stop();
                return (false, sw.ElapsedMilliseconds, $"InfluxDB 连接失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _client?.Dispose();
                _client = null;
            }
        }

        /// <inheritdoc/>
        public async Task<(bool Success, long LatencyMs, string Message)> TestConnectionAsync(DatabaseConfig config)
        {
            if (config == null)
            {
                return (false, 0, "配置为空。");
            }

            InfluxDBClient? client = null;
            try
            {
                var url = BuildUrl(config);
                var token = config.Token ?? string.Empty;
                client = new InfluxDBClient(url, token);

                var sw = Stopwatch.StartNew();
                await client.PingAsync();
                sw.Stop();
                return (true, sw.ElapsedMilliseconds, "InfluxDB 连接正常。");
            }
            catch (Exception ex)
            {
                return (false, 0, $"InfluxDB 连接失败：{ex.Message}");
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <inheritdoc/>
        public async Task<(bool Success, string Message)> DeleteBeforeAsync(DateTime cutoffUtc)
        {
            InfluxDBClient? client;
            string bucket, org;
            lock (_lock)
            {
                client = _client;
                bucket = _bucket;
                org = _org;
            }

            if (client == null || string.IsNullOrEmpty(bucket))
            {
                return (false, "尚未配置 InfluxDB 历史库连接，无法执行历史清理。");
            }

            try
            {
                var deleteApi = client.GetDeleteApi();
                // 删除范围：epoch 起点 ~ cutoff；谓词限定 measurement，避免波及同 bucket 其他数据。
                await deleteApi.Delete(
                    new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    cutoffUtc,
                    $"_measurement=\"{MeasurementName}\"",
                    bucket,
                    org);
                return (true, $"已删除 {cutoffUtc:yyyy-MM-dd HH:mm:ss} UTC 之前的时序数据。");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InfluxDB 历史清理失败（cutoff={Cutoff}）。", cutoffUtc);
                return (false, $"InfluxDB 历史清理失败: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<(bool Success, long Rows, string Message)> ExportAllAsync(string outputCsvPath)
        {
            InfluxDBClient? client;
            string bucket, org;
            lock (_lock)
            {
                client = _client;
                bucket = _bucket;
                org = _org;
            }

            if (client == null || string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(org))
            {
                return (false, 0, "尚未配置 InfluxDB 历史库连接，跳过时序数据导出。");
            }

            try
            {
                var flux = $"from(bucket: \"{Escape(bucket)}\")\n" +
                           "  |> range(start: 0)\n" +
                           $"  |> filter(fn: (r) => r._measurement == \"{MeasurementName}\")";

                var csv = await client.GetQueryApi().QueryRawAsync(flux, null, org);
                if (string.IsNullOrEmpty(csv))
                {
                    // 空结果也写出文件（仅表头缺失，保留空文件以标记已导出）
                    await System.IO.File.WriteAllTextAsync(outputCsvPath, string.Empty);
                    return (true, 0, "InfluxDB 时序数据为空，已导出空文件。");
                }

                await System.IO.File.WriteAllTextAsync(outputCsvPath, csv);
                // 行数 = 换行符数（InfluxDB 原生 CSV 每行一个数据点/注释）
                var rows = csv.Count(c => c == '\n');
                return (true, rows, $"已导出 {rows} 行时序数据。");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InfluxDB 全量导出失败。");
                return (false, 0, $"InfluxDB 全量导出失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 组装 FLUX 查询：双 tag 过滤 + （可选）窗口聚合 + pivot 合并字段 + 倒序取 limit 再升序返回。
        /// <paramref name="aggregateWindowMs"/> 大于 0 时插入 aggregateWindow，用于大时间范围降采样。
        /// <paramref name="aggregateFn"/> 聚合函数，白名单校验（mean/max/min/first/last），防止 FLUX 注入。
        /// </summary>
        private static string BuildQuery(
            string bucket,
            string deviceKey,
            string variableKey,
            int limit,
            DateTime? start,
            DateTime? end,
            long? aggregateWindowMs = null,
            string aggregateFn = "mean")
        {
            var startExpr = start.HasValue ? $"'{start.Value:O}'" : "-30d";
            var stopExpr = end.HasValue ? $", stop: '{end.Value:O}'" : string.Empty;

            var fn = NormalizeAggregateFn(aggregateFn);

            var aggregate = aggregateWindowMs is > 0
                ? $"  |> aggregateWindow(every: {aggregateWindowMs}ms, fn: {fn}, createEmpty: false)\n"
                : string.Empty;

            return $"from(bucket: \"{Escape(bucket)}\")\n" +
                   $"  |> range(start: {startExpr}{stopExpr})\n" +
                   $"  |> filter(fn: (r) => r._measurement == \"{MeasurementName}\" " +
                   $"and r.device_key == '{Escape(deviceKey)}' and r.variable_key == '{Escape(variableKey)}')\n" +
                   aggregate +
                   "  |> pivot(rowKey:[\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")\n" +
                   "  |> sort(columns: [\"_time\"], desc: true)\n" +
                   $"  |> limit(n: {limit})\n" +
                   "  |> sort(columns: [\"_time\"], desc: false)";
        }

        /// <summary>
        /// 聚合函数白名单归一化：仅允许 mean/max/min/first/last，其余回退 mean（防 FLUX 注入）。
        /// </summary>
        private static string NormalizeAggregateFn(string? fn) =>
            fn switch
            {
                "max" => "max",
                "min" => "min",
                "first" => "first",
                "last" => "last",
                _ => "mean"
            };

        /// <summary>
        /// FLUX 字符串字面量转义（反斜杠与单引号）。
        /// </summary>
        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("'", "\\'");

        private static double ToDouble(object? value)
        {
            return value switch
            {
                double d => d,
                decimal m => (double)m,
                long l => l,
                int i => i,
                float f => f,
                string s when double.TryParse(s, out var d) => d,
                _ => 0
            };
        }

        private static string BuildUrl(DatabaseConfig config)
        {
            var host = config.Host;
            if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                host = $"http://{host}";
            }
            return $"{host}:{config.Port}";
        }
    }
}
