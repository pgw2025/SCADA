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
            await CreateDefaultWidgetTemplatesAsync();
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
    /// 初始化内置 HMI 组件模板种子数据（24 条，幂等：仅补缺）。
    /// 数据来源：P0 一次性脚本从前端 widgetRegistry 序列化生成（审查 B2），不手写。
    /// 系统内置模板（IsSystem=true）可编辑/隐藏/排序，禁止删除（见 HmiWidgetTemplateAppService.DeleteAsync）。
    /// </summary>
    private async Task CreateDefaultWidgetTemplatesAsync()
    {
        var seeds = BuildBuiltinWidgetTemplates();
        var existingKeys = await _db.Set<HmiWidgetTemplate>()
            .Select(t => t.TemplateKey)
            .ToListAsync();

        var toAdd = seeds.Where(s => !existingKeys.Contains(s.TemplateKey)).ToList();
        if (toAdd.Count == 0) return;

        await _db.Set<HmiWidgetTemplate>().AddRangeAsync(toAdd);
        await _db.SaveChangesAsync();
        _logger.LogInformation("内置组件模板种子初始化：{Count} 条", toAdd.Count);
    }

    /// <summary>
    /// 内置组件模板种子构造（24 条，由 P0 一次性脚本从 widgetRegistry 求值生成）。
    /// </summary>
    private static List<HmiWidgetTemplate> BuildBuiltinWidgetTemplates() => new()
    {
            new HmiWidgetTemplate
            {
                TemplateKey = "boiler",
                RenderType = "boiler",
                Name = "加热锅炉反应釜",
                Category = "equipment",
                Description = "工业超温蒸汽燃煤锅炉，带火焰动态变频效果。",
                DefaultWidth = 140,
                DefaultHeight = 180,
                IconKind = "lucide",
                IconKey = "battery-charging",
                IconColor = "text-amber-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 10
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "pump",
                RenderType = "pump",
                Name = "离心输送水泵",
                Category = "equipment",
                Description = "液体或气体加压叶轮主输水泵，运行自带叶片旋转效果。",
                DefaultWidth = 70,
                DefaultHeight = 70,
                IconKind = "lucide",
                IconKey = "cpu",
                IconColor = "text-emerald-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 20
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "valve",
                RenderType = "valve",
                Name = "智能两位电磁阀",
                Category = "equipment",
                Description = "蝶阀/电磁球阀，状态切换时蝶阀手轮旋转90°。",
                DefaultWidth = 60,
                DefaultHeight = 60,
                IconKind = "lucide",
                IconKey = "toggle-left",
                IconColor = "text-indigo-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#10b981\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 30
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "tank",
                RenderType = "tank",
                Name = "圆角储液容器罐",
                Category = "equipment",
                Description = "带刻度及气泡波纹的液体深度容器。",
                DefaultWidth = 120,
                DefaultHeight = 160,
                IconKind = "lucide",
                IconKey = "layers",
                IconColor = "text-sky-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 40
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "conveyor",
                RenderType = "conveyor",
                Name = "变频滚轮传送带",
                Category = "equipment",
                Description = "物料或箱体传动物件传送带，速度非零时展现位移动画。",
                DefaultWidth = 260,
                DefaultHeight = 40,
                IconKind = "lucide",
                IconKey = "workflow",
                IconColor = "text-orange-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 50
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "motor",
                RenderType = "motor",
                Name = "变频伺服AC电机",
                Category = "equipment",
                Description = "变频配给驱动电机，工作时伴随冷却风扇叶极速旋转效果。",
                DefaultWidth = 120,
                DefaultHeight = 90,
                IconKind = "lucide",
                IconKey = "refresh-cw",
                IconColor = "text-sky-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 60
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "gauge-dial",
                RenderType = "gauge-dial",
                Name = "高精度机械表盘",
                Category = "sensors",
                Description = "圆形度盘表，支持设置极限阈值并同步变红警告。",
                DefaultWidth = 120,
                DefaultHeight = 120,
                IconKind = "lucide",
                IconKey = "gauge",
                IconColor = "text-purple-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":120,\"minValue\":0,\"unit\":\"℃\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 70
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "gauge-level",
                RenderType = "gauge-level",
                Name = "液位刻度警告柱",
                Category = "sensors",
                Description = "带有高、中、低限阈值的段式刻度检测条。",
                DefaultWidth = 50,
                DefaultHeight = 140,
                IconKind = "lucide",
                IconKey = "thermometer",
                IconColor = "text-rose-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 80
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "digital-val",
                RenderType = "digital-val",
                Name = "多功能数显仪表",
                Category = "sensors",
                Description = "工业LED高亮七段数值显示面板，可绑定任意PLC点。",
                DefaultWidth = 130,
                DefaultHeight = 60,
                IconKind = "lucide",
                IconKey = "tv",
                IconColor = "text-cyan-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 90
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "var-display",
                RenderType = "var-display",
                Name = "数据变量显示框",
                Category = "sensors",
                Description = "大字号数值显示，可设小数位与阈值变色；开启「可设定」后点击弹出数字键盘写入变量。",
                DefaultWidth = 150,
                DefaultHeight = 70,
                IconKind = "lucide",
                IconKey = "hash",
                IconColor = "text-lime-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":null,\"thresholdMin\":null,\"decimals\":2,\"settable\":false,\"writeMin\":null,\"writeMax\":null,\"confirmRequired\":false,\"showBorder\":false,\"borderColor\":\"#cbd5e1\",\"borderWidth\":1.5,\"borderStyle\":\"solid\",\"borderRadius\":8,\"showBackground\":false,\"bgColor\":\"#ffffff\",\"showInnerLabel\":false,\"enableAlarmBorder\":true}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 100
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "multi-var-dashboard",
                RenderType = "multi-var-dashboard",
                Name = "实时多变量看板",
                Category = "sensors",
                Description = "多变量实时聚合看板：支持多变量绑定、列数调节(1~6列/自适应)、边框与底色、阈值报警指示及卡片/表格/紧凑三种排版。",
                DefaultWidth = 360,
                DefaultHeight = 240,
                IconKind = "lucide",
                IconKey = "layout-dashboard",
                IconColor = "text-sky-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10,\"dashboardTitle\":\"实时参数监控看板\",\"showDashboardTitle\":true,\"dashboardTitleBgColor\":\"\",\"dashboardTitleColor\":\"\",\"dashboardLayout\":\"grid\",\"dashboardColumns\":2,\"dashboardGap\":8,\"showBorder\":true,\"borderColor\":\"#cbd5e1\",\"borderWidth\":1.5,\"borderStyle\":\"solid\",\"borderRadius\":8,\"showBackground\":true,\"bgColor\":\"#ffffff\",\"dashboardShowItemBorder\":true,\"dashboardItemBorderColor\":\"#e2e8f0\",\"dashboardItemBgColor\":\"#f8fafc\",\"dashboardValueFontSize\":16,\"dashboardLabelFontSize\":11,\"dashboardZebra\":false,\"dashboardTheme\":\"pure-white\",\"dashboardItems\":[{\"id\":\"item-1\",\"variableKey\":\"boiler_temp\",\"label\":\"锅炉温度\",\"unit\":\"℃\",\"precision\":1,\"showStatusDot\":true,\"thresholdMin\":20,\"thresholdMax\":90},{\"id\":\"item-2\",\"variableKey\":\"boiler_press\",\"label\":\"主管道压力\",\"unit\":\"MPa\",\"precision\":2,\"showStatusDot\":true,\"thresholdMin\":null,\"thresholdMax\":8.5},{\"id\":\"item-3\",\"variableKey\":\"tank_level\",\"label\":\"储罐液位\",\"unit\":\"%\",\"precision\":1,\"showStatusDot\":true,\"thresholdMin\":15,\"thresholdMax\":95},{\"id\":\"item-4\",\"variableKey\":\"pump_state\",\"label\":\"主循环泵\",\"unit\":\"\",\"precision\":null,\"showStatusDot\":true,\"thresholdMin\":null,\"thresholdMax\":null}]}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 110
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "trend-chart",
                RenderType = "trend-chart",
                Name = "实时波段趋势图",
                Category = "sensors",
                Description = "动态微积分平滑滤波趋势图，记录历史PLC模拟参数，支持多变量序列与逐线颜色/粗细自定义。",
                DefaultWidth = 280,
                DefaultHeight = 160,
                IconKind = "lucide",
                IconKey = "activity",
                IconColor = "text-red-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10,\"trendSeries\":[],\"trendShowLegend\":true,\"trendLegendFontSize\":9,\"trendUseGlobalRange\":true,\"trendAxisMode\":\"absolute\",\"trendAxisMin\":null,\"trendAxisMax\":null,\"trendShowGrid\":true,\"trendShowAxisLabels\":true,\"trendAxisLabelFontSize\":8,\"trendShowPointValues\":false,\"trendPointValueFontSize\":8,\"trendPointValueColor\":\"auto\",\"trendPointValueEveryN\":null}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 120
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "led",
                RenderType = "led",
                Name = "高发光LED指示灯",
                Category = "sensors",
                Description = "红绿双色状态警告信源灯，支持光晕频闪效果。",
                DefaultWidth = 40,
                DefaultHeight = 50,
                IconKind = "div",
                IconKey = "div-led",
                IconColor = "",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#10b981\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 130
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "sys-time",
                RenderType = "sys-time",
                Name = "实时系统时钟",
                Category = "sensors",
                Description = "数字式数码时钟控件，秒级刷新显示当前时间。",
                DefaultWidth = 160,
                DefaultHeight = 50,
                IconKind = "lucide",
                IconKey = "clock",
                IconColor = "text-emerald-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 140
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "pipe-h",
                RenderType = "pipe-h",
                Name = "水平输水管路",
                Category = "structures",
                Description = "支持流向光带闪烁动效的水平流动金属管。",
                DefaultWidth = 160,
                DefaultHeight = 16,
                IconKind = "div",
                IconKey = "div-h",
                IconColor = "",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 150
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "pipe-v",
                RenderType = "pipe-v",
                Name = "垂直高压管道",
                Category = "structures",
                Description = "支持流速频闪的垂直重力回流水管。",
                DefaultWidth = 16,
                DefaultHeight = 160,
                IconKind = "div",
                IconKey = "div-v",
                IconColor = "",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 160
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "text",
                RenderType = "text",
                Name = "自定义文本组态",
                Category = "structures",
                Description = "静态或者动态映射文字说明，可调节字号和对齐方式。",
                DefaultWidth = 120,
                DefaultHeight = 35,
                IconKind = "lucide",
                IconKey = "type",
                IconColor = "text-slate-300",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 170
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "rounded-btn",
                RenderType = "rounded-btn",
                Name = "圆角按钮",
                Category = "structures",
                Description = "可绑定变量的高级圆角控制按钮：背景/操作变量可分离绑定，支持取反/置位/复位/按1送0/设值/画面跳转/执行脚本，内置启动/停止/复位/点动/急停 5 种预设风格。",
                DefaultWidth = 110,
                DefaultHeight = 46,
                IconKind = "lucide",
                IconKey = "sparkles",
                IconColor = "text-emerald-500",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10,\"buttonMode\":\"toggle\",\"borderRadius\":10,\"borderWidth\":1,\"strokeColor\":\"#38bdf8\",\"buttonText\":\"圆角按钮\",\"state0Text\":\"OFF 停止\",\"state0BgColor\":\"#1e293b\",\"state0TextColor\":\"#94a3b8\",\"state1Text\":\"ON 运行\",\"state1BgColor\":\"#0284c7\",\"state1TextColor\":\"#ffffff\",\"customStates\":\"0:停止:#334155:#94a3b8;1:运行:#0284c7:#ffffff;2:报警:#dc2626:#ffffff\",\"targetPageId\":null,\"targetScriptId\":null,\"showModeBadge\":true,\"opDeviceId\":null,\"opVariableKey\":null,\"presetStyle\":\"\"}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 180
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "switch",
                RenderType = "switch",
                Name = "两位旋动选择按钮",
                Category = "structures",
                Description = "自复位旋钮式状态控制开关，触手可及。",
                DefaultWidth = 70,
                DefaultHeight = 90,
                IconKind = "lucide",
                IconKey = "toggle-right",
                IconColor = "text-[#1890ff]",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":12,\"bold\":false,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 190
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "image",
                RenderType = "image",
                Name = "自定义图片",
                Category = "structures",
                Description = "上传或从图库选择图片作为图元，可缩放、跨页面复用。",
                DefaultWidth = 200,
                DefaultHeight = 150,
                IconKind = "lucide",
                IconKey = "image",
                IconColor = "text-sky-400",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"imageUrl\":\"\",\"imageFit\":\"fill\"}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 200
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "title-header-tech-desktop",
                RenderType = "title-header",
                Name = "科技蓝·大屏标题栏",
                Category = "headers",
                Description = "经典未来科技蓝宽屏大屏标题栏，带晶蓝发光切角翼展、数字时钟与在线状态。",
                DefaultWidth = 960,
                DefaultHeight = 72,
                IconKind = "lucide",
                IconKey = "monitor",
                IconColor = "text-sky-400",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":22,\"bold\":true,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10,\"headerStyle\":\"tech-blue\",\"headerDevice\":\"desktop\",\"headerTitle\":\"工业互联网智能监控大屏\",\"headerSubtitle\":\"INTELLIGENT SCADA MONITORING PLATFORM\",\"headerLogoText\":\"SCADA 5G\",\"headerShowClock\":true,\"headerShowStatus\":true,\"headerStatusText\":\"系统运行正常\",\"headerGlowColor\":\"#38bdf8\"}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 210
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "title-header-tech-mobile",
                RenderType = "title-header",
                Name = "科技蓝·移动标题栏",
                Category = "headers",
                Description = "科技蓝移动竖屏标题栏，紧凑机能流光切角与紧凑状态指示点。",
                DefaultWidth = 375,
                DefaultHeight = 56,
                IconKind = "lucide",
                IconKey = "smartphone",
                IconColor = "text-sky-400",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"activeColor\":\"#3b82f6\",\"inactiveColor\":\"#94a3b8\",\"maxValue\":100,\"minValue\":0,\"unit\":\"\",\"showValue\":false,\"showLabel\":false,\"fontSize\":16,\"bold\":true,\"align\":\"center\",\"thresholdMax\":90,\"thresholdMin\":10,\"headerStyle\":\"tech-blue\",\"headerDevice\":\"mobile\",\"headerTitle\":\"车间移动监控中心\",\"headerSubtitle\":\"MOBILE SCADA TERMINAL\",\"headerLogoText\":\"5G\",\"headerShowClock\":false,\"headerShowStatus\":true,\"headerStatusText\":\"在线\",\"headerGlowColor\":\"#38bdf8\"}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 220
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "nav-menu-desktop",
                RenderType = "nav-menu",
                Name = "桌面端·顶部导航条",
                Category = "headers",
                Description = "桌面端横向导航菜单条：图标+文字+同端画面跳转，3~5 项，运行态自动高亮当前画面。",
                DefaultWidth = 960,
                DefaultHeight = 56,
                IconKind = "lucide",
                IconKey = "panel-top",
                IconColor = "text-sky-400",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"menuStyle\":\"navy-midnight\",\"menuDevice\":\"desktop\",\"menuItems\":[{\"icon\":\"home\",\"text\":\"总览\",\"targetPageId\":null},{\"icon\":\"factory\",\"text\":\"工艺监控\",\"targetPageId\":null},{\"icon\":\"bell\",\"text\":\"报警中心\",\"targetPageId\":null}],\"menuAccentColor\":\"#38bdf8\",\"menuFontSize\":14}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 230
            },
            new HmiWidgetTemplate
            {
                TemplateKey = "nav-menu-mobile",
                RenderType = "nav-menu",
                Name = "移动端·底部标签栏",
                Category = "headers",
                Description = "移动端底部 Tab 导航栏：图标+文字+同端画面跳转，3~5 项，运行态自动高亮当前画面。",
                DefaultWidth = 375,
                DefaultHeight = 64,
                IconKind = "lucide",
                IconKey = "smartphone",
                IconColor = "text-emerald-400",
                RenderKind = "builtin",
                SvgTemplate = null,
                DefaultPropsJson = "{\"menuStyle\":\"navy-midnight\",\"menuDevice\":\"mobile\",\"menuItems\":[{\"icon\":\"home\",\"text\":\"首页\",\"targetPageId\":null},{\"icon\":\"line-chart\",\"text\":\"趋势\",\"targetPageId\":null},{\"icon\":\"bell\",\"text\":\"报警\",\"targetPageId\":null}],\"menuAccentColor\":\"#38bdf8\",\"menuFontSize\":12}",
                PropSchemaJson = "[]",
                IsSystem = true,
                SortOrder = 240
            }
    };


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
