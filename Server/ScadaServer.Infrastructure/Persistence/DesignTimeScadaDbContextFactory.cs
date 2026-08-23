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
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=scada_design;Uid=root;Pwd=root;",
            new MySqlServerVersion(new Version(8, 0, 36)));

        return new ScadaDbContext(optionsBuilder.Options);
    }
}
