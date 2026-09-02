using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Runtime.Tasks
{
    /// <summary>
    /// 历史清理执行器：删除 InfluxDB variable_history 测量中超过保留期的时序数据。
    /// <para>参数（ParamsJson）：retentionDays（必填，≥1）。</para>
    /// </summary>
    public class ClearHistoryTaskExecutor : IScheduledTaskExecutor
    {
        private readonly IInfluxStore _influxStore;
        private readonly ILogger<ClearHistoryTaskExecutor> _logger;

        public ClearHistoryTaskExecutor(IInfluxStore influxStore, ILogger<ClearHistoryTaskExecutor> logger)
        {
            _influxStore = influxStore;
            _logger = logger;
        }

        public string Type => ScheduledTaskTypes.ClearHistory;

        public async Task<string> ExecuteAsync(ScheduledTask task, CancellationToken token)
        {
            var retentionDays = ParseRetentionDays(task.ParamsJson);
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            var (success, message) = await _influxStore.DeleteBeforeAsync(cutoff);
            if (!success)
            {
                throw new InvalidOperationException(message);
            }

            return $"保留 {retentionDays} 天：{message}";
        }

        private int ParseRetentionDays(string? paramsJson)
        {
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson).RootElement;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "计划任务参数不是合法 JSON: {ParamsJson}", paramsJson);
                throw new InvalidOperationException($"任务参数不是合法 JSON: {ex.Message}");
            }

            if (!root.TryGetProperty("retentionDays", out var el) || el.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException("缺少保留天数参数（retentionDays）");
            }
            var days = el.GetInt32();
            if (days < 1)
            {
                throw new InvalidOperationException("保留天数必须 ≥ 1 天");
            }
            return days;
        }
    }
}
