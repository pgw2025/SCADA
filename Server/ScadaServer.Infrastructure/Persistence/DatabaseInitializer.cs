using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SqlSugar;
using ScadaServer.Domain.Entities;


namespace ScadaServer.Infrastructure.Persistence;

public class DatabaseInitializer
{
    private readonly ISqlSugarClient _db;
    private readonly ILogger<DatabaseInitializer> _logger;

    private const string CurrentVersion = "1.0.0";

    public DatabaseInitializer(
        ISqlSugarClient db,
        ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 初始化数据库
    /// </summary>
    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始初始化数据库...");

            await _db.Ado.BeginTranAsync();

            CreateTables();
            await SeedDataAsync();
            await SaveDbVersionAsync();

            await _db.Ado.CommitTranAsync();

            _logger.LogInformation("数据库初始化完成");
        }
        catch (Exception ex)
        {
            await _db.Ado.RollbackTranAsync();

            _logger.LogError(
                ex,
                "数据库初始化失败");

            throw;
        }
    }

    /// <summary>
    /// 自动扫描实体并建表
    /// </summary>
    private void CreateTables()
    {
        try
        {
            _logger.LogInformation("开始创建数据表...");

            var entityTypes = typeof(EntityBase)
                .Assembly
                .GetTypes()
                .Where(t =>
                    t.IsClass &&
                    !t.IsAbstract &&
                    typeof(EntityBase).IsAssignableFrom(t))
                .ToArray();

            _db.CodeFirst.InitTables(entityTypes);

            _logger.LogInformation(
                "数据表创建完成，共发现 {Count} 个实体",
                entityTypes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建数据表失败");
            throw;
        }
    }

    /// <summary>
    /// 初始化种子数据
    /// </summary>
    private async Task SeedDataAsync()
    {
        try
        {
            _logger.LogInformation("开始初始化种子数据...");

            await CreateDefaultAreaAsync();
            await CreateDefaultAdminAsync();

            _logger.LogInformation("种子数据初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化种子数据失败");
            throw;
        }
    }

    private async Task CreateDefaultAreaAsync()
    {
        var exists = await _db.Queryable<Area>()
            .AnyAsync();

        if (exists)
            return;

        await _db.Insertable(new Area
        {
            Name = "默认区域",
            Description = ""
        }).ExecuteCommandAsync();

        _logger.LogInformation("默认区域创建成功");
    }

    private async Task CreateDefaultAdminAsync()
    {
        var exists = await _db.Queryable<SystemUser>()
            .AnyAsync();

        if (exists)
            return;

        var admin = new SystemUser
        {
            Username = "admin",
            Role = "Admin",
            Status = "Active"
        };

        var passwordHasher =
            new PasswordHasher<SystemUser>();

        admin.PasswordHash =
            passwordHasher.HashPassword(
                admin,
                "123456");

        await _db.Insertable(admin)
            .ExecuteCommandAsync();

        _logger.LogWarning(
            "默认管理员账号已创建: admin/123456");
    }

    /// <summary>
    /// 保存数据库版本
    /// </summary>
    private async Task SaveDbVersionAsync()
    {
        try
        {
            var exists = await _db.Queryable<DbVersion>()
                .AnyAsync(v => v.Version == CurrentVersion);

            if (exists)
                return;

            await _db.Insertable(new DbVersion
            {
                Version = CurrentVersion,
                AppliedAt = DateTime.Now
            }).ExecuteCommandAsync();

            _logger.LogInformation(
                "数据库版本记录完成: {Version}",
                CurrentVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存数据库版本失败");
            throw;
        }
    }
}