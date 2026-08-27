using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
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
        // 数据库连接配置已写入 WebApi 项目的 appsettings.json（含密码），
        // 设计时从配置文件读取，不再依赖命令行环境变量 SystemDbConfig__Password。
        // 同时兼容从解决方案根目录 / WebApi 目录 / 当前目录运行 dotnet ef。
        var basePath = Directory.GetCurrentDirectory();
        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("ScadaServer.WebApi/appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("Server/ScadaServer.WebApi/appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var section = config.GetSection("SystemDbConfig");
        var host = section["Host"] ?? "localhost";
        var port = section["Port"] ?? "3306";
        var database = section["DatabaseName"] ?? "scada";
        var user = section["Username"] ?? "root";
        var password = section["Password"] ?? "";
        var connStr = $"Server={host};Port={port};Database={database};Uid={user};Pwd={password};";

        var optionsBuilder = new DbContextOptionsBuilder<ScadaDbContext>();
        // 设计时固定 ServerVersion，避免 AutoDetect 触发真实连接（生成迁移只需模型）
        optionsBuilder.UseMySql(
            connStr,
            new MySqlServerVersion(new Version(8, 0, 36)));

        return new ScadaDbContext(optionsBuilder.Options);
    }
}
