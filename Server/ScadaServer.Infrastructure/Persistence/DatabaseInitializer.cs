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
            // 需解析的设备变量：延迟加载设备数据模型与协议，以拿到 DriverKey 判别协议。
            var rows = await _db.Set<DataPointMapping>()
                .Include(dv => dv.Device)!.ThenInclude(d => d!.Model).ThenInclude(m => m!.Protocol)
                .Where(dv => dv.AddressConfigJson == null && dv.Address != null && dv.Address != "")
                .ToListAsync();

            if (rows.Count == 0) return;

            var changed = 0;
            foreach (var dv in rows)
            {
                var driverKey = dv.Device?.Model?.Protocol?.DriverKey;
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
    /// 一次性回填（阶段 3）：把散落在 <c>Device.JsonConfig</c> 的连接参数抽取为
    /// <see cref="Controller"/>（每设备独占一个，Code = "PLC{Device.Id}"）+ <see cref="DeviceConnection"/>
    /// （ConfigJson 保存原 JsonConfig 原文），并回填 <c>Device.ControllerId</c> / <c>Device.ConnectionId</c>。
    /// <para>
    /// 幂等：仅处理 <c>Device.ConnectionId IS NULL</c> 的设备，重复执行不产生重复行；
    /// 若上次执行中断残留了同名 Controller（Code = PLC{Id}），自动复用而非重建。
    /// </para>
    /// <para>
    /// 设计说明：本方法采用项目内已有的一次性回填先例（<see cref="BackfillDataPointMappingAddressConfigAsync"/>），
    /// 而非 EF 迁移内注入 DbContext——回填与结构迁移分离、可独立重试，符合阶段 3 双读兼容期的低风险要求。
    /// </para>
    /// </summary>
    private async Task BackfillControllerAndConnectionAsync()
    {
        try
        {
            // 待回填设备：尚未建立默认连接（ConnectionId IS NULL），含空配置（JsonConfig NULL）设备。
            var devices = await _db.Set<Device>()
                .Include(d => d.Model)!.ThenInclude(m => m!.Protocol)
                .Where(d => d.ConnectionId == null)
                .ToListAsync();

            if (devices.Count == 0) return;

            var now = DateTime.UtcNow;
            var successCount = 0;
            var skippedCount = 0;

            foreach (var device in devices)
            {
                try
                {
                    // 设备所绑模型的协议即设备协议（现状：DataModel.ProtocolId 必填）。
                    var protocol = device.Model?.Protocol;
                    if (protocol == null)
                    {
                        _logger.LogWarning("回填连接跳过设备 {Key}：缺少数据模型或协议。", device.Key);
                        skippedCount++;
                        continue;
                    }

                    // 1) 控制器：独占一个（Code = PLC{Device.Id} 保证唯一）；残留同名控制器则复用。
                    var controllerCode = $"PLC{device.Id}";
                    var controller = await _db.Set<Controller>()
                        .FirstOrDefaultAsync(c => c.Code == controllerCode);

                    if (controller == null)
                    {
                        controller = new Controller
                        {
                            Code = controllerCode,
                            Name = DeviceConnectionProfile.Truncate($"{device.Name} 控制器", 100) ?? string.Empty,
                            ProtocolId = protocol.Id,
                            Manufacturer = DeviceConnectionProfile.Truncate(device.Model?.Vendor, 100),
                            Model = DeviceConnectionProfile.Truncate(device.Model?.ModelName, 100),
                            IsEnabled = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        await _db.Set<Controller>().AddAsync(controller);
                        await _db.SaveChangesAsync();   // 取得 controller.Id
                    }

                    // 2) 解析连接冗余列（Host/Port/超时）。仅用于管理/检索展示，ConfigJson 原文才是运行真相源。
                    var json = device.JsonConfig ?? "{}";
                    var parsed = DeviceConnectionProfile.ParseConnectionSummary(protocol.DriverKey, json);

                    var connection = new DeviceConnection
                    {
                        ControllerId = controller.Id,
                        Name = DeviceConnectionProfile.Truncate($"{device.Name} 连接", 100) ?? string.Empty,
                        ProtocolId = protocol.Id,
                        Host = parsed.Host,
                        Port = parsed.Port,
                        ConfigJson = json,
                        TimeoutMs = parsed.TimeoutMs,
                        ReconnectIntervalMs = 5000,
                        IsEnabled = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    await _db.Set<DeviceConnection>().AddAsync(connection);
                    await _db.SaveChangesAsync();   // 取得 connection.Id

                    // 3) 回填设备默认连接指向。
                    device.ControllerId = controller.Id;
                    device.ConnectionId = connection.Id;
                    device.UpdatedAt = now;
                    await _db.SaveChangesAsync();

                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "回填连接失败：设备 {Key}（已跳过，可重跑）。", device.Key);
                    skippedCount++;
                }
            }

            _logger.LogInformation(
                "连接回填完成：共处理 {Total} 台设备，成功 {Success}，跳过/失败 {Skipped}。",
                devices.Count, successCount, skippedCount);
        }
        catch (Exception ex)
        {
            // 回填属一次性迁移增强，失败不应阻断启动（运行时双读兼容层仍可经 JsonConfig 回退运行）。
            _logger.LogError(ex, "回填控制器/连接（BackfillControllerAndConnection）失败，已跳过；可重跑修复。");
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
