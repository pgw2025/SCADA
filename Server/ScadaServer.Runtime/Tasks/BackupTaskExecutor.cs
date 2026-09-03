using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Runtime.Tasks
{
    /// <summary>
    /// 数据备份执行器：将 MySQL 业务数据（JSON）与 InfluxDB 时序历史（CSV）打包导出为 zip 备份文件。
    /// <para>
    /// 导出范围：MySQL 全部业务表（不含 SystemLogs/ConfigLogs/DbVersions 运行日志与迁移簿记，
    /// 以及已迁移至 InfluxDB 的 VariableHistory 遗留表）+ InfluxDB variable_history 全量 CSV。
    /// 备份目录由配置 ScheduledTasks:BackupOutputDir 指定（默认 Backups，相对服务内容根目录）。
    /// </para>
    /// </summary>
    public class BackupTaskExecutor : IScheduledTaskExecutor
    {
        /// <summary>MySQL 导出时排除的表（日志/簿记/已迁移 InfluxDB 的遗留数据），写入清单供恢复方知悉。</summary>
        private static readonly string[] ExcludedTables =
        {
            "SystemLogs", "ConfigLogs", "DbVersions", "VariableHistories"
        };

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IInfluxStore _influxStore;
        private readonly IHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BackupTaskExecutor> _logger;

        public BackupTaskExecutor(
            IServiceScopeFactory scopeFactory,
            IInfluxStore influxStore,
            IHostEnvironment environment,
            IConfiguration configuration,
            ILogger<BackupTaskExecutor> logger)
        {
            _scopeFactory = scopeFactory;
            _influxStore = influxStore;
            _environment = environment;
            _configuration = configuration;
            _logger = logger;
        }

        public string Type => ScheduledTaskTypes.Backup;

        public async Task<string> ExecuteAsync(ScheduledTask task, CancellationToken token)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var stagingDir = Path.Combine(Path.GetTempPath(), $"scada_backup_{timestamp}_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(stagingDir);

                // 1. MySQL 业务表导出为 mysql_tables.json
                var tableCounts = await ExportMySqlAsync(stagingDir, token);

                // 2. InfluxDB 时序数据全量导出为 influx_variable_history.csv
                var influxCsv = Path.Combine(stagingDir, "influx_variable_history.csv");
                var (influxOk, influxRows, influxMessage) = await _influxStore.ExportAllAsync(influxCsv);

                // 3. 备份清单
                var manifest = new
                {
                    BackupTime = DateTime.UtcNow,
                    Server = _environment.EnvironmentName,
                    MySqlTables = tableCounts,
                    MySqlExcluded = ExcludedTables,
                    InfluxExported = influxOk,
                    InfluxRows = influxRows,
                    InfluxNote = influxOk ? null : influxMessage
                };
                await File.WriteAllTextAsync(
                    Path.Combine(stagingDir, "manifest.json"),
                    JsonSerializer.Serialize(manifest, JsonOptions), token);

                // 4. 打包为 zip（输出目录可由 ScheduledTasks:BackupOutputDir 配置，默认 Backups）
                var outputDir = Path.Combine(
                    _environment.ContentRootPath,
                    _configuration["ScheduledTasks:BackupOutputDir"] ?? "Backups");
                Directory.CreateDirectory(outputDir);
                var zipPath = Path.Combine(outputDir, $"backup_{timestamp}.zip");

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                ZipFile.CreateFromDirectory(stagingDir, zipPath);

                var sizeKb = new FileInfo(zipPath).Length / 1024;
                return $"备份完成：{zipPath}（{sizeKb} KB，MySQL {tableCounts.Values.Sum()} 行，" +
                       $"Influx {(influxOk ? $"{influxRows} 行" : "未导出")}）";
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingDir))
                    {
                        Directory.Delete(stagingDir, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "备份临时目录清理失败：{Dir}", stagingDir);
                }
            }
        }

        /// <summary>
        /// 导出 MySQL 业务表到 mysql_tables.json，返回 表名 → 行数。
        /// <para>显式列出业务表（排除 SystemLogs/ConfigLogs/DbVersions 日志簿记与已迁移 InfluxDB 的 VariableHistory 遗留表）。</para>
        /// </summary>
        private async Task<Dictionary<string, int>> ExportMySqlAsync(string stagingDir, CancellationToken token)
        {
            var data = new Dictionary<string, object>();
            var counts = new Dictionary<string, int>();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

            async Task AddAsync<T>(string name, IQueryable<T> source) where T : class
            {
                var rows = await source.AsNoTracking().ToListAsync(token);
                data[name] = rows;
                counts[name] = rows.Count;
            }

            await AddAsync(nameof(db.AlarmRules), db.AlarmRules);
            await AddAsync(nameof(db.AlarmRecords), db.AlarmRecords);
            await AddAsync(nameof(db.LinkageRules), db.LinkageRules);
            await AddAsync(nameof(db.Areas), db.Areas);
            await AddAsync(nameof(db.DatabaseConfigs), db.DatabaseConfigs);
            await AddAsync(nameof(db.DataConversions), db.DataConversions);
            await AddAsync(nameof(db.DataModels), db.DataModels);
            await AddAsync(nameof(db.Devices), db.Devices);
            await AddAsync(nameof(db.DataPointMappings), db.DataPointMappings);
            await AddAsync(nameof(db.ExposedInterfaces), db.ExposedInterfaces);
            await AddAsync(nameof(db.HmiComponents), db.HmiComponents);
            await AddAsync(nameof(db.DataPoints), db.DataPoints);
            await AddAsync(nameof(db.Protocols), db.Protocols);
            await AddAsync(nameof(db.MqttServers), db.MqttServers);
            await AddAsync(nameof(db.MqttVariableConfigs), db.MqttVariableConfigs);
            await AddAsync(nameof(db.ScadaPages), db.ScadaPages);
            await AddAsync(nameof(db.ScadaProjects), db.ScadaProjects);
            await AddAsync(nameof(db.ScheduledTasks), db.ScheduledTasks);
            await AddAsync(nameof(db.Sensors), db.Sensors);
            await AddAsync(nameof(db.SystemConfigs), db.SystemConfigs);
            await AddAsync(nameof(db.SystemScripts), db.SystemScripts);
            await AddAsync(nameof(db.ScriptExecutionRecords), db.ScriptExecutionRecords);
            await AddAsync(nameof(db.SystemUsers), db.SystemUsers);
            await AddAsync(nameof(db.VariableRealtimes), db.VariableRealtimes);

            await System.IO.File.WriteAllTextAsync(
                Path.Combine(stagingDir, "mysql_tables.json"),
                JsonSerializer.Serialize(data), token);

            _logger.LogInformation("备份：MySQL 导出 {Tables} 张表共 {Rows} 行。", counts.Count, counts.Values.Sum());
            return counts;
        }
    }
}
