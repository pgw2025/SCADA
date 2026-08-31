using System.Diagnostics;
using System.Globalization;
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
    /// InfluxDB 时序库访问实现。
    ///
    /// 线程安全策略：
    /// 1. 不使用 lock。
    /// 2. 使用 Volatile.Read/Write 保证配置读取的内存可见性。
    /// 3. 使用 Interlocked.Exchange 原子替换 InfluxDBClient。
    /// 4. 使用 ClientHolder 引用计数，避免 Rebuild/Dispose 时提前释放正在使用的 Client。
    ///
    /// 行协议：
    /// measurement = variable_history
    /// tags = device_key + variable_key
    /// fields = value/raw_value/quality/device_id/variable_name
    ///
    /// series 身份 = device_key + variable_key，
    /// 避免 device_id、variable_name、quality 等高基数字段成为 tag。
    /// </summary>
    public sealed class InfluxStore : IInfluxStore, IDisposable
    {
        public const string MeasurementName = "variable_history";

        private const int MaxWriteRetry = 3;

        private readonly ILogger<InfluxStore> _logger;

        /*
         * 当前 ClientHolder。
         *
         * 使用 Volatile.Read / Interlocked.Exchange。
         *
         * 不直接保存 InfluxDBClient，是因为 Rebuild 时不能立即 Dispose
         * 一个可能仍然正在被其他线程使用的 Client。
         */
        private ClientHolder? _holder;

        private int _disposed;

        public InfluxStore(ILogger<InfluxStore> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 当前 InfluxDB 是否已经配置。
        /// </summary>
        public bool IsConfigured
        {
            get
            {
                var holder = Volatile.Read(ref _holder);

                return holder != null &&
                       !string.IsNullOrWhiteSpace(holder.Bucket);
            }
        }

        /// <inheritdoc/>
        public void Rebuild(DatabaseConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                _logger.LogWarning(
                    "InfluxStore 已经释放，忽略 Rebuild 请求。");

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
                _logger.LogError(
                    ex,
                    "创建 InfluxDB 客户端失败（Url={Url}）。",
                    url);

                return;
            }

            var bucket = !string.IsNullOrWhiteSpace(config.Bucket)
                ? config.Bucket
                : config.DatabaseName;

            var org = config.Org ?? string.Empty;

            if (string.IsNullOrWhiteSpace(bucket))
            {
                _logger.LogWarning(
                    "InfluxDB 配置缺少 Bucket，无法启用 InfluxDB。Url={Url}",
                    url);

                newClient.Dispose();
                return;
            }

            var newHolder = new ClientHolder(
                newClient,
                bucket,
                org);

            /*
             * 原子替换。
             *
             * 旧 Holder 不在这里直接 Dispose。
             * 因为可能还有其他线程正在使用旧 Holder。
             *
             * 新 Holder 成为当前 Client 后，
             * 后续请求都会获取新 Client。
             *
             * 旧 Holder 会在引用计数归零后自动 Dispose。
             */
            var oldHolder = Interlocked.Exchange(
                ref _holder,
                newHolder);

            oldHolder?.MarkRetired();

            _logger.LogInformation(
                "InfluxDB 历史库配置已生效：Url={Url}, Bucket={Bucket}, Org={Org}。",
                url,
                bucket,
                org);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteAsync(List<VariableHistory> points)
        {
            if (points == null || points.Count == 0)
            {
                return true;
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                _logger.LogWarning(
                    "InfluxStore 已经释放，无法执行批量写入。");

                return false;
            }

            var holder = AcquireHolder();

            if (holder == null)
            {
                return false;
            }

            try
            {
                var client = holder.Client;
                var bucket = holder.Bucket;
                var org = holder.Org;

                if (string.IsNullOrWhiteSpace(bucket) ||
                    string.IsNullOrWhiteSpace(org))
                {
                    _logger.LogWarning(
                        "InfluxDB Bucket 或 Org 未配置，无法执行批量写入。");

                    return false;
                }

                var data = BuildPoints(points);

                if (data.Count == 0)
                {
                    return true;
                }

                var writeApi = client.GetWriteApiAsync();

                for (var attempt = 1; attempt <= MaxWriteRetry; attempt++)
                {
                    try
                    {
                        await writeApi.WritePointsAsync(
                            data,
                            bucket,
                            org);

                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "InfluxDB 批量写入失败（第 {Attempt}/{MaxRetry} 次，共 {Count} 点）。",
                            attempt,
                            MaxWriteRetry,
                            data.Count);

                        if (attempt < MaxWriteRetry)
                        {
                            await Task.Delay(
                                TimeSpan.FromSeconds(attempt));
                        }
                    }
                }

                return false;
            }
            finally
            {
                holder.Release();
            }
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

            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.FromResult(
                    new List<HistoryRecordDto>());
            }

            var holder = AcquireHolder();

            if (holder == null)
            {
                return Task.FromResult(
                    new List<HistoryRecordDto>());
            }

            try
            {
                var client = holder.Client;
                var bucket = holder.Bucket;
                var org = holder.Org;

                if (string.IsNullOrWhiteSpace(bucket) ||
                    string.IsNullOrWhiteSpace(org))
                {
                    return Task.FromResult(
                        new List<HistoryRecordDto>());
                }

                var query = BuildQuery(
                    bucket,
                    deviceKey,
                    variableKey,
                    limit,
                    start,
                    end,
                    aggregateWindowMs,
                    aggregateFn);

                var tables = client
                    .GetQueryApiSync()
                    .QuerySync(query, org);

                var result = new List<HistoryRecordDto>();

                foreach (var table in tables)
                {
                    foreach (var record in table.Records)
                    {
                        var time = record.GetTimeInDateTime();

                        var timestamp =
                            time ?? DateTime.MinValue;

                        result.Add(
                            new HistoryRecordDto
                            {
                                Id = 0,
                                VariableKey = variableKey,

                                VariableName =
                                    record.GetValueByKey(
                                        "variable_name") as string
                                    ?? variableKey,

                                Value = ToDouble(
                                    record.GetValueByKey("value")),

                                RawValue =
                                    record.GetValueByKey(
                                        "raw_value") as string,

                                Timestamp = timestamp,

                                Quality =
                                    record.GetValueByKey(
                                        "quality") as string
                            });
                    }
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "InfluxDB 历史查询失败：device={DeviceKey}, variable={VariableKey}。",
                    deviceKey,
                    variableKey);

                return Task.FromResult(
                    new List<HistoryRecordDto>());
            }
            finally
            {
                holder.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<(bool Success, long LatencyMs, string Message)>
            PingAsync()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return (
                    false,
                    0,
                    "InfluxStore 已经释放。");
            }

            var holder = AcquireHolder();

            if (holder == null)
            {
                return (
                    false,
                    0,
                    "尚未配置 InfluxDB 连接。");
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    await holder.Client.PingAsync();

                    stopwatch.Stop();

                    return (
                        true,
                        stopwatch.ElapsedMilliseconds,
                        "InfluxDB 连接正常。");
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();

                    return (
                        false,
                        stopwatch.ElapsedMilliseconds,
                        $"InfluxDB 连接失败：{ex.Message}");
                }
            }
            finally
            {
                holder.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<(bool Success, long LatencyMs, string Message)>
            TestConnectionAsync(DatabaseConfig config)
        {
            if (config == null)
            {
                return (
                    false,
                    0,
                    "配置为空。");
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                return (
                    false,
                    0,
                    "InfluxStore 已经释放。");
            }

            InfluxDBClient? client = null;

            try
            {
                var url = BuildUrl(config);
                var token = config.Token ?? string.Empty;

                client = new InfluxDBClient(
                    url,
                    token);

                var stopwatch = Stopwatch.StartNew();

                await client.PingAsync();

                stopwatch.Stop();

                return (
                    true,
                    stopwatch.ElapsedMilliseconds,
                    "InfluxDB 连接正常。");
            }
            catch (Exception ex)
            {
                return (
                    false,
                    0,
                    $"InfluxDB 连接失败：{ex.Message}");
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <inheritdoc/>
        public async Task<(bool Success, string Message)>
            DeleteBeforeAsync(DateTime cutoffUtc)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return (
                    false,
                    "InfluxStore 已经释放。");
            }

            var holder = AcquireHolder();

            if (holder == null)
            {
                return (
                    false,
                    "尚未配置 InfluxDB 历史库连接，无法执行历史清理。");
            }

            try
            {
                if (string.IsNullOrWhiteSpace(holder.Bucket) ||
                    string.IsNullOrWhiteSpace(holder.Org))
                {
                    return (
                        false,
                        "InfluxDB Bucket 或 Org 未配置，无法执行历史清理。");
                }

                /*
                 * 确保 cutoff 为 UTC。
                 */
                cutoffUtc = cutoffUtc.ToUniversalTime();

                var deleteApi =
                    holder.Client.GetDeleteApi();

                /*
                 * 删除范围：
                 *
                 * epoch -> cutoffUtc
                 *
                 * 谓词限定 measurement，
                 * 避免影响同一个 bucket 中的其他数据。
                 */
                await deleteApi.Delete(
                    new DateTime(
                        1970,
                        1,
                        1,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc),

                    cutoffUtc,

                    $"_measurement=\"{EscapeForPredicate(MeasurementName)}\"",

                    holder.Bucket,
                    holder.Org);

                return (
                    true,
                    $"已删除 {cutoffUtc:yyyy-MM-dd HH:mm:ss} UTC 之前的时序数据。");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "InfluxDB 历史清理失败（cutoff={Cutoff}）。",
                    cutoffUtc);

                return (
                    false,
                    $"InfluxDB 历史清理失败: {ex.Message}");
            }
            finally
            {
                holder.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<(bool Success, long Rows, string Message)>
            ExportAllAsync(string outputCsvPath)
        {
            if (string.IsNullOrWhiteSpace(outputCsvPath))
            {
                return (
                    false,
                    0,
                    "导出路径不能为空。");
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                return (
                    false,
                    0,
                    "InfluxStore 已经释放。");
            }

            var holder = AcquireHolder();

            if (holder == null)
            {
                return (
                    false,
                    0,
                    "尚未配置 InfluxDB 历史库连接，跳过时序数据导出。");
            }

            try
            {
                if (string.IsNullOrWhiteSpace(holder.Bucket) ||
                    string.IsNullOrWhiteSpace(holder.Org))
                {
                    return (
                        false,
                        0,
                        "InfluxDB Bucket 或 Org 未配置。");
                }

                var flux =
                    $"from(bucket: \"{Escape(holder.Bucket)}\")\n" +
                    "  |> range(start: 0)\n" +
                    $"  |> filter(fn: (r) => r._measurement == \"{Escape(MeasurementName)}\")";

                var csv =
                    await holder.Client
                        .GetQueryApi()
                        .QueryRawAsync(
                            flux,
                            null,
                            holder.Org);

                if (string.IsNullOrEmpty(csv))
                {
                    /*
                     * 空结果也写出文件。
                     */
                    await System.IO.File.WriteAllTextAsync(
                        outputCsvPath,
                        string.Empty);

                    return (
                        true,
                        0,
                        "InfluxDB 时序数据为空，已导出空文件。");
                }

                await System.IO.File.WriteAllTextAsync(
                    outputCsvPath,
                    csv);

                /*
                 * InfluxDB 原生 CSV：
                 * 每一行对应 CSV 中的一行。
                 *
                 * 注意：
                 * 这里统计的是换行数量，而不是严格意义上的
                 * 数据点数量，因为 CSV 中可能包含注释/表头。
                 */
                var rows = csv.Count(
                    c => c == '\n');

                return (
                    true,
                    rows,
                    $"已导出 {rows} 行时序数据。");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "InfluxDB 全量导出失败。");

                return (
                    false,
                    0,
                    $"InfluxDB 全量导出失败: {ex.Message}");
            }
            finally
            {
                holder.Release();
            }
        }

        /// <summary>
        /// 获取当前 ClientHolder 的一个使用引用。
        ///
        /// Acquire 过程完全不使用 lock。
        /// </summary>
        private ClientHolder? AcquireHolder()
        {
            while (true)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return null;
                }

                var holder = Volatile.Read(ref _holder);

                if (holder == null)
                {
                    return null;
                }

                if (!holder.TryAcquire())
                {
                    /*
                     * Holder 正在退出生命周期，
                     * 重新读取当前 Holder。
                     */
                    continue;
                }

                /*
                 * 防止 Dispose/Rebuild 在 Acquire 前后发生竞争。
                 *
                 * 如果 Holder 已经被替换，
                 * 当前 Holder 仍然可以安全使用，
                 * 因为引用计数保证它不会提前 Dispose。
                 */
                return holder;
            }
        }

        /// <summary>
        /// 将历史数据转换为 InfluxDB PointData。
        /// </summary>
        private static List<PointData> BuildPoints(
            IReadOnlyList<VariableHistory> points)
        {
            var data = new List<PointData>(
                points.Count);

            foreach (var p in points)
            {
                var point =
                    PointData
                        .Measurement(MeasurementName)
                        .Tag(
                            "device_key",
                            p.DeviceKey ?? string.Empty)
                        .Tag(
                            "variable_key",
                            p.VariableKey ?? string.Empty)
                        .Field(
                            "value",
                            p.Value)
                        .Field(
                            "device_id",
                            p.DeviceId);

                if (!string.IsNullOrEmpty(p.RawValue))
                {
                    point = point.Field(
                        "raw_value",
                        p.RawValue);
                }

                if (!string.IsNullOrEmpty(p.Quality))
                {
                    point = point.Field(
                        "quality",
                        p.Quality);
                }

                if (!string.IsNullOrEmpty(p.VariableName))
                {
                    point = point.Field(
                        "variable_name",
                        p.VariableName);
                }

                /*
                 * InfluxDB 时间戳统一使用 UTC。
                 */
                var timestamp =
                    p.Timestamp.ToUniversalTime();

                point =
                    point.Timestamp(
                        timestamp,
                        WritePrecision.Ns);

                data.Add(point);
            }

            return data;
        }

        /// <summary>
        /// 组装 FLUX 查询。
        ///
        /// 查询流程：
        ///
        /// 1. range
        /// 2. measurement + device_key + variable_key 过滤
        /// 3. 可选 aggregateWindow
        /// 4. pivot 合并 fields
        /// 5. 时间倒序
        /// 6. limit
        /// 7. 时间正序
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
            var startExpr =
                start.HasValue
                    ? $"'{start.Value.ToUniversalTime():O}'"
                    : "-30d";

            var stopExpr =
                end.HasValue
                    ? $", stop: '{end.Value.ToUniversalTime():O}'"
                    : string.Empty;

            var fn =
                NormalizeAggregateFn(
                    aggregateFn);

            var aggregate =
                aggregateWindowMs is > 0
                    ? $"  |> aggregateWindow(every: {aggregateWindowMs}ms, fn: {fn}, createEmpty: false)\n"
                    : string.Empty;

            return
                $"from(bucket: \"{Escape(bucket)}\")\n" +
                $"  |> range(start: {startExpr}{stopExpr})\n" +
                $"  |> filter(fn: (r) => r._measurement == \"{Escape(MeasurementName)}\" " +
                $"and r.device_key == '{Escape(deviceKey)}' " +
                $"and r.variable_key == '{Escape(variableKey)}')\n" +
                aggregate +
                "  |> pivot(rowKey:[\"_time\"], columnKey: [\"_field\"], valueColumn: \"_value\")\n" +
                "  |> sort(columns: [\"_time\"], desc: true)\n" +
                $"  |> limit(n: {limit})\n" +
                "  |> sort(columns: [\"_time\"], desc: false)";
        }

        /// <summary>
        /// 聚合函数白名单。
        ///
        /// 仅允许：
        /// mean / max / min / first / last
        /// </summary>
        private static string NormalizeAggregateFn(
            string? fn)
        {
            return fn?.ToLowerInvariant() switch
            {
                "max" => "max",
                "min" => "min",
                "first" => "first",
                "last" => "last",
                _ => "mean"
            };
        }

        /// <summary>
        /// FLUX 双引号字符串转义。
        /// </summary>
        private static string Escape(
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        /// <summary>
        /// FLUX predicate 字符串转义。
        /// </summary>
        private static string EscapeForPredicate(
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        /// <summary>
        /// InfluxDB 返回值转换为 double。
        /// </summary>
        private static double ToDouble(
            object? value)
        {
            return value switch
            {
                double d => d,

                float f => f,

                decimal m => (double)m,

                long l => l,

                int i => i,

                short s => s,

                byte b => b,

                string s when
                    double.TryParse(
                        s,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var d)
                    => d,

                _ => 0d
            };
        }

        /// <summary>
        /// 构造 InfluxDB URL。
        /// </summary>
        private static string BuildUrl(
            DatabaseConfig config)
        {
            var host =
                config.Host?.Trim()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(host))
            {
                host = "localhost";
            }

            if (!host.StartsWith(
                    "http://",
                    StringComparison.OrdinalIgnoreCase) &&
                !host.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase))
            {
                host = $"http://{host}";
            }

            /*
             * 如果 Host 本身已经包含端口，
             * 则不重复追加。
             */
            if (Uri.TryCreate(
                    host,
                    UriKind.Absolute,
                    out var uri) &&
                uri.Port > 0 &&
                uri.IsDefaultPort == false)
            {
                return host.TrimEnd('/');
            }

            return
                $"{host.TrimEnd('/')}:{config.Port}";
        }

        /// <summary>
        /// InfluxDB Client 生命周期管理器。
        ///
        /// 使用引用计数解决：
        ///
        /// Rebuild
        ///     ↓
        /// 替换 Client
        ///     ↓
        /// 旧 Client 不能马上 Dispose
        ///     ↓
        /// 等所有请求完成
        ///     ↓
        /// 最后一个请求 Release
        ///     ↓
        /// Dispose 旧 Client
        ///
        /// 全程不使用 lock。
        /// </summary>
        private sealed class ClientHolder
        {
            private int _referenceCount = 1;

            private int _retired;

            private int _disposed;

            public InfluxDBClient Client { get; }

            public string Bucket { get; }

            public string Org { get; }

            public ClientHolder(
                InfluxDBClient client,
                string bucket,
                string org)
            {
                Client = client;
                Bucket = bucket;
                Org = org;
            }

            /// <summary>
            /// 尝试获取一个使用引用。
            ///
            /// 初始引用为 1，
            /// 代表当前 Holder 被 Store 持有。
            /// </summary>
            public bool TryAcquire()
            {
                while (true)
                {
                    /*
                     * 已经退出当前生命周期，
                     * 不再允许新的请求进入。
                     */
                    if (Volatile.Read(ref _retired) != 0)
                    {
                        return false;
                    }

                    var current =
                        Volatile.Read(
                            ref _referenceCount);

                    if (current <= 0)
                    {
                        return false;
                    }

                    if (Interlocked.CompareExchange(
                            ref _referenceCount,
                            current + 1,
                            current) == current)
                    {
                        /*
                         * 极端竞争：
                         *
                         * TryAcquire 成功后，
                         * 另一个线程可能刚好 MarkRetired。
                         *
                         * 即便如此也没关系：
                         * 我们已经拿到了引用，
                         * Release 会保证 Client 不会被提前释放。
                         */
                        return true;
                    }
                }
            }

            /// <summary>
            /// 当前 Holder 不再是 Store 的活动 Client。
            /// </summary>
            public void MarkRetired()
            {
                if (Interlocked.Exchange(
                        ref _retired,
                        1) == 0)
                {
                    Release();
                }
            }

            /// <summary>
            /// 释放一个使用引用。
            /// </summary>
            public void Release()
            {
                var count =
                    Interlocked.Decrement(
                        ref _referenceCount);

                if (count == 0 &&
                    Volatile.Read(ref _retired) != 0)
                {
                    DisposeClient();
                }
            }

            private void DisposeClient()
            {
                if (Interlocked.Exchange(
                        ref _disposed,
                        1) != 0)
                {
                    return;
                }

                try
                {
                    Client.Dispose();
                }
                catch
                {
                    /*
                     * Dispose 不能影响其他请求。
                     *
                     * 这里不向外抛异常。
                     */
                }
            }
        }

        /// <summary>
        /// 释放 InfluxStore。
        ///
        /// 不直接 Dispose 当前 Client，
        /// 而是：
        ///
        /// 1. 原子清空当前 Holder
        /// 2. 标记旧 Holder retired
        /// 3. 等正在执行的请求全部 Release
        /// 4. 最后自动 Dispose Client
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(
                    ref _disposed,
                    1) != 0)
            {
                return;
            }

            var oldHolder =
                Interlocked.Exchange(
                    ref _holder,
                    null);

            oldHolder?.MarkRetired();
        }
    }
}

