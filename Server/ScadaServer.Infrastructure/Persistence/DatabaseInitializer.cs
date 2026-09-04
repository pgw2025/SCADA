using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScadaServer.Application.Services;
using ScadaServer.Domain.Addresses;
using ScadaServer.Domain.Constants;
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
            await BackfillDataPointMappingAddressConfigAsync();
            await BackfillControllerAndConnectionAsync();
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
    /// 协议是"数据模型如何通信"的真相源（Protocol.Key 决定运行期驱动派发）。
    /// 这里预置系统当前支持的协议：已实现驱动的（S7 / OPCUA / VIRTUAL）默认启用，可立即用于创建设备；
    /// 尚未实现驱动的（MODBUSTCP / MQTT）默认停用，仅在 <c>ProtocolDriverFactory</c> 实现对应驱动后启用。
    /// </summary>
    private async Task CreateDefaultProtocolsAsync()
    {
        var protocols = new[]
        {
            new { Key = "S7",       Name = "Siemens S7",        IsEnabled = true,  Description = "西门子 S7 系列 PLC（S7-1200/1500 等）" },
            new { Key = "OPCUA",    Name = "OPC UA",            IsEnabled = true,  Description = "OPC UA 客户端连接" },
            new { Key = "VIRTUAL",  Name = "虚拟设备",          IsEnabled = true,  Description = "内存虚拟设备（模拟 / 演示）" },
            new { Key = "MODBUSTCP",Name = "Modbus TCP",        IsEnabled = false, Description = "Modbus TCP 从站（驱动待开发，暂停用）" },
            new { Key = "MQTT",     Name = "MQTT 订阅",         IsEnabled = false, Description = "MQTT 订阅源（驱动待开发，暂停用）" }
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
                IsEnabled = p.IsEnabled,
                Description = p.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
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
            Role = SystemRoles.Admin,
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
            "默认管理员账号已创建: admin/123456。此为开发占位弱口令，请立即修改，避免尚未改密即接入生产。");
    }

    /// <summary>
    /// 一次性回填：为历史 <c>DataPointMapping.Address</c>（旧展示串）生成结构化
    /// <c>AddressConfigJson</c>（JSON 权威源）。此后地址以 JSON 为唯一信任，展示串由后端重新生成。
    /// <para>
    /// 幂等：仅处理 <c>AddressConfigJson</c> 为空、<c>Address</c> 非空且协议解析成功的行；
    /// 无法解析的旧地址保持 JSON 为空、原字符串保留，交由前端后续人工重配。
    /// </para>
    /// </summary>
    private async Task BackfillDataPointMappingAddressConfigAsync()
    {
        try
        {
            // 需解析的设备变量：延迟加载设备连接，以拿到所附连接的协议键判别协议。
            var rows = await _db.Set<DataPointMapping>()
                .Include(dv => dv.Device)!.ThenInclude(d => d!.Connection).ThenInclude(c => c!.Protocol)
                .Where(dv => dv.AddressConfigJson == null && dv.Address != null && dv.Address != "")
                .ToListAsync();

            if (rows.Count == 0) return;

            var changed = 0;
            foreach (var dv in rows)
            {
                var driverKey = dv.Device?.Connection?.Protocol?.Key;
                var protocol = driverKey?.Trim().ToUpperInvariant() switch
                {
                    "S7" or "S7DRIVER" => "S7",
                    "OPCUA" or "OPCUADRIVER" => "OPCUA",
                    "MODBUSTCP" or "MODBUSTCPDRIVER" => "Modbus",
                    _ => null
                };
                if (protocol == null) continue;

                var config = AddressConfigSerializer.BuildFromDisplay(dv.Address, protocol);
                if (config == null) continue; // 无法解析，保留原字符串，交由前端补配

                dv.AddressConfigJson = AddressConfigSerializer.Serialize(config);
                changed++;
            }

            if (changed > 0)
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("已回填 {Count} 条设备变量的结构化地址（AddressConfigJson）。", changed);
            }
        }
        catch (Exception ex)
        {
            // 回填属尽力而为，失败不应阻断启动
            _logger.LogError(ex, "回填设备变量结构化地址（AddressConfigJson）失败，已跳过。");
        }
    }

    /// <summary>
    /// 连接不变量审计（自阶段 3）：
    /// 数据模型不再绑定协议后，设备协议唯一真相源为所附连接，无法再按"模型协议"自动合成连接。
    /// 此处仅检测 <c>Device.ConnectionId IS NULL</c> 的遗留设备并告警，提示需在高级模式下人工附加控制器 / 连接；
    /// 不再自动创建。注意：DataModel 已无 <c>Protocol</c> 导航，故此处不再 Include 模型协议。
    /// </summary>
    private async Task BackfillControllerAndConnectionAsync()
    {
        try
        {
            var devices = await _db.Set<Device>()
                .Where(d => d.ConnectionId == null)
                .ToListAsync();

            if (devices.Count == 0) return;

            foreach (var device in devices)
            {
                _logger.LogWarning(
                    "遗留设备 {Key}(Id={BaseId}) 尚未附加连接，运行期无法派发驱动。请在设备管理的高级模式下手动关联控制器与连接。",
                    device.Key, device.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接不变量审计（BackfillControllerAndConnection）失败，已跳过；可重跑检测。");
        }
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
                AppliedAt = DateTime.UtcNow
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
