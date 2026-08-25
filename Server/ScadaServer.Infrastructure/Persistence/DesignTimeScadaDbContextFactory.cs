using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace ScadaServer.Infrastructure.Persistence;

/// <summary>
/// 设计时 DbContext 工厂，供 EF Tools（Add-Migration / dotnet ef migrations）使用。
/// 仅用于迁移生成，不连接真实数据库。
/// </summary>
public class DesignTimeScadaDbContextFactory : IDesignTimeDbContextFactory<ScadaDbContext>
{
    public ScadaDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ScadaDbContext>();
        // 设计时固定 ServerVersion，避免 AutoDetect 触发真实连接（生成迁移只需模型）
        // 连接参数与运行时对齐（Database=scada），密码不硬编码，由环境变量 SystemDbConfig__Password 注入
        var host = GetEnv("SystemDbConfig__Host", "localhost");
        var port = GetEnv("SystemDbConfig__Port", "3306");
        var database = GetEnv("SystemDbConfig__DatabaseName", "scada");
        var user = GetEnv("SystemDbConfig__Username", "root");
        var password = GetEnv("SystemDbConfig__Password", "");
        var connStr = $"Server={host};Port={port};Database={database};Uid={user};Pwd={password};";

        optionsBuilder.UseMySql(
            connStr,
            new MySqlServerVersion(new Version(8, 0, 36)));

        return new ScadaDbContext(optionsBuilder.Options);
    }

    private static string GetEnv(string name, string fallback)
        => Environment.GetEnvironmentVariable(name) ?? fallback;
}
