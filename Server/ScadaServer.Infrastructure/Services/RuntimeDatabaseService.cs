using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.Options;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Influx;

namespace ScadaServer.Infrastructure.Services
{
    /// <summary>
    /// 运行时数据库管理服务实现。
    /// <para>
    /// 主库（MySQL）配置读写 override 文件 <c>appsettings.dboverride.json</c>，
    /// 该文件在启动时叠加到 appsettings.json 之上（见 Program.cs）；修改后需重启生效。
    /// 连接测试：MySQL 用 MySqlConnector，InfluxDB 复用 <see cref="IInfluxStore"/> 的独立客户端探测。
    /// </para>
    /// </summary>
    public class RuntimeDatabaseService : IRuntimeDatabaseService
    {
        private const string OverrideFileName = "appsettings.dboverride.json";
        private const string SecretMask = "******";

        private readonly IOptions<SystemDbOptions> _dbOptions;
        private readonly IHostEnvironment _env;
        private readonly ILogger<RuntimeDatabaseService> _logger;
        private readonly IInfluxStore _influxStore;

        public RuntimeDatabaseService(
            IOptions<SystemDbOptions> dbOptions,
            IHostEnvironment env,
            ILogger<RuntimeDatabaseService> logger,
            IInfluxStore influxStore)
        {
            _dbOptions = dbOptions;
            _env = env;
            _logger = logger;
            _influxStore = influxStore;
        }

        /// <inheritdoc/>
        public Task<MainDatabaseConfigDto> GetMainConfigAsync()
        {
            var o = _dbOptions.Value;
            return Task.FromResult(new MainDatabaseConfigDto
            {
                Host = o.Host,
                Port = o.Port,
                DatabaseName = o.DatabaseName,
                Username = o.Username,
                Password = string.IsNullOrEmpty(o.Password) ? null : SecretMask,
                HasPassword = !string.IsNullOrEmpty(o.Password)
            });
        }

        /// <inheritdoc/>
        public async Task SaveMainConfigAsync(MainDatabaseConfigDto dto)
        {
            if (dto == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(dto.Host) || dto.Port <= 0 ||
                string.IsNullOrWhiteSpace(dto.DatabaseName) || string.IsNullOrWhiteSpace(dto.Username))
            {
                throw new ScadaServer.Domain.Exceptions.BusinessException("主库配置的 主机/端口/库名/用户名 均不能为空。");
            }

            var existing = _dbOptions.Value;
            // 掩码/空 => 保持原密码不变
            var password = (string.IsNullOrEmpty(dto.Password) || dto.Password == SecretMask)
                ? existing.Password
                : dto.Password;

            var payload = new Dictionary<string, object>
            {
                ["SystemDbConfig"] = new Dictionary<string, object>
                {
                    ["Host"] = dto.Host.Trim(),
                    ["Port"] = dto.Port,
                    ["DatabaseName"] = dto.DatabaseName.Trim(),
                    ["Username"] = dto.Username.Trim(),
                    ["Password"] = password
                }
            };

            var path = GetOverridePath();
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(path, json);
            _logger.LogInformation("主库配置已写入 override 文件：{Path}（重启后生效）。", path);
        }

        /// <inheritdoc/>
        public async Task<TestConnectionResult> TestConnectionAsync(TestConnectionRequest request)
        {
            if (request == null)
            {
                return new TestConnectionResult { Success = false, Message = "请求为空。" };
            }

            var backend = request.BackendType?.Trim() ?? string.Empty;
            if (backend.Equals("MySQL", StringComparison.OrdinalIgnoreCase))
            {
                return await TestMySqlAsync(request);
            }

            if (backend.Equals("InfluxDB", StringComparison.OrdinalIgnoreCase))
            {
                return await TestInfluxAsync(request);
            }

            return new TestConnectionResult { Success = false, Message = $"不支持的数据库后端：{backend}。" };
        }

        private async Task<TestConnectionResult> TestMySqlAsync(TestConnectionRequest request)
        {
            var connStr = $"Server={request.Host};Port={request.Port};Database={request.DatabaseName};Uid={request.Username};Pwd={request.Password};";
            var sw = Stopwatch.StartNew();
            try
            {
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                sw.Stop();
                return new TestConnectionResult { Success = true, LatencyMs = sw.ElapsedMilliseconds, Message = "MySQL 连接成功。" };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new TestConnectionResult { Success = false, LatencyMs = sw.ElapsedMilliseconds, Message = $"MySQL 连接失败：{ex.Message}" };
            }
        }

        private async Task<TestConnectionResult> TestInfluxAsync(TestConnectionRequest request)
        {
            var config = new DatabaseConfig
            {
                Host = request.Host,
                Port = request.Port,
                Org = request.Org,
                Bucket = string.IsNullOrWhiteSpace(request.Bucket) ? request.DatabaseName : request.Bucket,
                DatabaseName = request.DatabaseName,
                Token = request.Token
            };

            var (success, latency, message) = await _influxStore.TestConnectionAsync(config);
            return new TestConnectionResult { Success = success, LatencyMs = latency, Message = message };
        }

        private string GetOverridePath() =>
            Path.Combine(_env.ContentRootPath, OverrideFileName);
    }
}