using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Runtime.Tasks
{
    /// <summary>
    /// 历史清理执行器：删除超过保留期的历史时序数据。
    /// <para>
    /// 双后端（阶段2）：已配置 InfluxDB 时清理 InfluxDB（可选 mysqlAlso=true 同时清 MySQL）；
    /// 未配置 InfluxDB 时兜底清理 MySQL（<see cref="IHistoryMaintenanceAppService"/>），不再直接失败。
    /// </para>
    /// <para>参数（ParamsJson）：retentionDays（必填，≥1）、mysqlAlso（可选 bool，默认 false）。</para>
    /// </summary>
    public class ClearHistoryTaskExecutor : IScheduledTaskExecutor
    {
        private readonly IInfluxStore _influxStore;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ClearHistoryTaskExecutor> _logger;

        public ClearHistoryTaskExecutor(
            IInfluxStore influxStore,
            IServiceScopeFactory scopeFactory,
            ILogger<ClearHistoryTaskExecutor> logger)
        {
            _influxStore = influxStore;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public string Type => ScheduledTaskTypes.ClearHistory;

        public async Task<string> ExecuteAsync(ScheduledTask task, CancellationToken token)
        {
            var (retentionDays, mysqlAlso) = ParseParams(task.ParamsJson);
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            // 已配置 InfluxDB → 清 InfluxDB（可选同时清 MySQL）
            if (_influxStore.IsConfigured)
            {
                var (success, message) = await _influxStore.DeleteBeforeAsync(cutoff);
                if (!success)
                {
                    throw new InvalidOperationException(message);
                }

                if (mysqlAlso)
                {
                    var mysqlDeleted = await CleanupMySqlAsync(cutoff, token);
                    return $"保留 {retentionDays} 天：{message}；同时清理 MySQL 历史 {mysqlDeleted} 条。";
                }

                return $"保留 {retentionDays} 天：{message}";
            }

            // 未配置 InfluxDB → 兜底清理 MySQL
            var deleted = await CleanupMySqlAsync(cutoff, token);
            return $"未配置时序库，已清理 MySQL 历史 {deleted} 条（保留 {retentionDays} 天）。";
        }

        /// <summary>清理 MySQL 历史数据（经 Application 层维护服务，分批删除）。</summary>
        private async Task<long> CleanupMySqlAsync(DateTime cutoffUtc, CancellationToken token)
        {
            using var scope = _scopeFactory.CreateScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IHistoryMaintenanceAppService>();
            return await maintenance.CleanupMySqlBeforeAsync(cutoffUtc, token);
        }

        private (int RetentionDays, bool MySqlAlso) ParseParams(string? paramsJson)
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

            var mysqlAlso = false;
            if (root.TryGetProperty("mysqlAlso", out var mysqlAlsoEl) && mysqlAlsoEl.ValueKind == JsonValueKind.True)
            {
                mysqlAlso = true;
            }

            return (days, mysqlAlso);
        }
    }
}

