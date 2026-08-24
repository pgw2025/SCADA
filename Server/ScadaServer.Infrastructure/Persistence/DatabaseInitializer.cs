using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScadaServer.Domain.Entities;
using ScadaServer.Infrastructure.Persistence;

namespace ScadaServer.Infrastructure.Persistence;

public class DatabaseInitializer
{
    private readonly ScadaDbContext _db;
    private readonly ILogger<DatabaseInitializer> _logger;

    private const string CurrentVersion = "1.0.0";

    public DatabaseInitializer(
        ScadaDbContext db,
        ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 初始化数据库（通过 EF Migrations 建库并写入种子数据）
    /// </summary>
    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始初始化数据库...");

            // 应用迁移（全新库会按 Migration 创建全部表结构）
            await _db.Database.MigrateAsync(cancellationToken);

            await SeedDataAsync();

            _logger.LogInformation("数据库初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "数据库初始化失败");

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
            await CreateDefaultProtocolsAsync();
            await CreateDefaultAdminAsync();
            await SaveDbVersionAsync();

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
        var exists = await _db.Set<Area>()
            .AnyAsync();

        if (exists)
            return;

        await _db.Set<Area>().AddAsync(new Area
        {
            Name = "默认区域",
            Description = ""
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("默认区域创建成功");
    }

    /// <summary>
    /// 初始化默认通信协议种子数据。
    /// <para>
    /// 协议是"数据模型如何通信"的真相源（Protocol.DriverKey 决定运行期驱动派发）。
    /// 这里预置系统当前支持的协议：已实现驱动的（S7 / OPCUA / VIRTUAL）默认启用，可立即用于创建设备；
    /// 尚未实现驱动的（MODBUSTCP / MQTT）默认停用，仅在 <c>ProtocolDriverFactory</c> 实现对应驱动后启用。
    /// </summary>
    private async Task CreateDefaultProtocolsAsync()
    {
        var protocols = new[]
        {
            new { Key = "S7",       Name = "Siemens S7",        DriverKey = "S7",       IsEnabled = true,  Description = "西门子 S7 系列 PLC（S7-1200/1500 等）" },
            new { Key = "OPCUA",    Name = "OPC UA",            DriverKey = "OPCUA",    IsEnabled = true,  Description = "OPC UA 客户端连接" },
            new { Key = "VIRTUAL",  Name = "虚拟设备",          DriverKey = "VIRTUAL",  IsEnabled = true,  Description = "内存虚拟设备（模拟 / 演示）" },
            new { Key = "MODBUSTCP",Name = "Modbus TCP",        DriverKey = "MODBUSTCP",IsEnabled = false, Description = "Modbus TCP 从站（驱动待开发，暂停用）" },
            new { Key = "MQTT",     Name = "MQTT 订阅",         DriverKey = "MQTT",     IsEnabled = false, Description = "MQTT 订阅源（驱动待开发，暂停用）" }
        };

        var existingKeys = await _db.Set<Protocol>()
            .Select(p => p.Key)
            .ToListAsync();

        foreach (var p in protocols)
        {
            if (existingKeys.Contains(p.Key))
                continue;

            await _db.Set<Protocol>().AddAsync(new Protocol
            {
                Key = p.Key,
                Name = p.Name,
                DriverKey = p.DriverKey,
                IsEnabled = p.IsEnabled,
                Description = p.Description,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("默认通信协议种子数据初始化完成");
    }

    private async Task CreateDefaultAdminAsync()
    {
        var exists = await _db.Set<SystemUser>()
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

        await _db.Set<SystemUser>().AddAsync(admin);
        await _db.SaveChangesAsync();

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
            var exists = await _db.Set<DbVersion>()
                .AnyAsync(v => v.Version == CurrentVersion);

            if (exists)
                return;

            await _db.Set<DbVersion>().AddAsync(new DbVersion
            {
                Version = CurrentVersion,
                AppliedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();

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
