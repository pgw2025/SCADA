using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ScadaServer.Domain.Entities;

namespace ScadaServer.Infrastructure.Persistence
{
    /// <summary>
    /// SCADA 数据库上下文（EF Core）
    /// </summary>
    public class ScadaDbContext : DbContext
    {
        /// <summary>
        /// Dictionary&lt;string,string&gt; 与 JSON 字符串互转的转换器（MySQL 不支持原生字典映射）
        /// </summary>
        private static readonly ValueConverter<Dictionary<string, string>?, string?> ExtensionDataConverter =
            new(v => ToJson(v), v => FromJson(v));

        /// <summary>
        /// 以序列化后的 JSON 字符串判断是否相等，保证字典内容变更能被追踪
        /// </summary>
        private static readonly ValueComparer<Dictionary<string, string>?> ExtensionDataComparer =
            new(
                (l, r) => ToJson(l) == ToJson(r),
                v => v == null ? 0 : ToJson(v)!.GetHashCode(),
                v => FromJson(ToJson(v)));

        private static string? ToJson(Dictionary<string, string>? value) =>
            value == null ? null : System.Text.Json.JsonSerializer.Serialize(value);

        private static Dictionary<string, string>? FromJson(string? value) =>
            string.IsNullOrEmpty(value) ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(value);

        public ScadaDbContext(DbContextOptions<ScadaDbContext> options) : base(options)
        {
        }

        #region DbSet

        public DbSet<AlarmRule> AlarmRules => Set<AlarmRule>();
        public DbSet<AlarmRecord> AlarmRecords => Set<AlarmRecord>();
        public DbSet<LinkageRule> LinkageRules => Set<LinkageRule>();
        public DbSet<Area> Areas => Set<Area>();
        public DbSet<ConfigLog> ConfigLogs => Set<ConfigLog>();
        public DbSet<Controller> Controllers => Set<Controller>();
        public DbSet<DatabaseConfig> DatabaseConfigs => Set<DatabaseConfig>();        public DbSet<DataConversion> DataConversions => Set<DataConversion>();
        public DbSet<DataModel> DataModels => Set<DataModel>();
        public DbSet<DbVersion> DbVersions => Set<DbVersion>();
        public DbSet<Device> Devices => Set<Device>();
        public DbSet<DeviceConnection> DeviceConnections => Set<DeviceConnection>();
        public DbSet<DeviceDataModel> DeviceDataModels => Set<DeviceDataModel>();
        public DbSet<DataPointMapping> DataPointMappings => Set<DataPointMapping>();
        public DbSet<ExposedInterface> ExposedInterfaces => Set<ExposedInterface>();
        public DbSet<HmiComponent> HmiComponents => Set<HmiComponent>();
        public DbSet<DataPoint> DataPoints => Set<DataPoint>();
        public DbSet<Protocol> Protocols => Set<Protocol>();
        public DbSet<MqttServer> MqttServers => Set<MqttServer>();
        public DbSet<MqttVariableConfig> MqttVariableConfigs => Set<MqttVariableConfig>();
        public DbSet<ScadaPage> ScadaPages => Set<ScadaPage>();
        public DbSet<ScadaProject> ScadaProjects => Set<ScadaProject>();
        public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
        public DbSet<Sensor> Sensors => Set<Sensor>();
        public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
        public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
        public DbSet<SystemScript> SystemScripts => Set<SystemScript>();
        public DbSet<ScriptExecutionRecord> ScriptExecutionRecords => Set<ScriptExecutionRecord>();
        public DbSet<SystemUser> SystemUsers => Set<SystemUser>();
        public DbSet<VariableHistory> VariableHistories => Set<VariableHistory>();
        public DbSet<VariableRealtime> VariableRealtimes => Set<VariableRealtime>();

        #endregion

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 表名（与现有库一致，显式声明避免 EF 复数约定差异）
            modelBuilder.Entity<AlarmRule>().ToTable("AlarmRules");

            // 报警记录表：索引支撑「列表/确认/清理/按设备」查询。
            // 数据量大，暂不建外键，避免级联删除/迁移开销影响运行时写入性能（同 VariableHistory 设计）。
            modelBuilder.Entity<AlarmRecord>().ToTable("AlarmRecords");
            modelBuilder.Entity<AlarmRecord>()
                .Property(r => r.Level).HasMaxLength(16).HasConversion<string>();
            modelBuilder.Entity<AlarmRecord>()
                .Property(r => r.Condition).HasMaxLength(16).HasConversion<string>();
            modelBuilder.Entity<AlarmRecord>()
                .Property(r => r.Source).HasMaxLength(16).HasConversion<string>();
            modelBuilder.Entity<AlarmRecord>()
                .HasIndex(r => r.TriggeredAt)
                .HasDatabaseName("ix_alarmrecord_triggeredat");
            modelBuilder.Entity<AlarmRecord>()
                .HasIndex(r => new { r.Acked, r.RecoveredAt })
                .HasDatabaseName("ix_alarmrecord_acked_recovered");
            modelBuilder.Entity<AlarmRecord>()
                .HasIndex(r => r.DeviceId)
                .HasDatabaseName("ix_alarmrecord_deviceid");
            modelBuilder.Entity<LinkageRule>().ToTable("LinkageRules");
            modelBuilder.Entity<Area>().ToTable("Areas");
            // 区域表：树形结构（自引用 ParentId）。列显式限长映射为 varchar
            // （Pomelo 对无长度 string 默认映射 longtext，无法建索引）；Code 唯一索引见 AddAreaCodeUniqueIndex 迁移。
            modelBuilder.Entity<Area>()
                .Property(a => a.Code).HasMaxLength(50);
            modelBuilder.Entity<Area>()
                .Property(a => a.Name).HasMaxLength(100);
            modelBuilder.Entity<Area>()
                .Property(a => a.Description).HasMaxLength(500);
            modelBuilder.Entity<Area>()
                .HasOne(a => a.Parent)
                .WithMany(a => a.Children)
                .HasForeignKey(a => a.ParentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Areas_Areas_ParentId");
            modelBuilder.Entity<Area>()
                .HasIndex(a => a.ParentId)
                .HasDatabaseName("ix_areas_parentid");
            // 区域编码唯一（NULL 允许多条共存；空字符串由迁移清洗为 NULL 后再建索引）。
            modelBuilder.Entity<Area>()
                .HasIndex(a => a.Code)
                .IsUnique()
                .HasDatabaseName("ix_areas_code");
            modelBuilder.Entity<ConfigLog>().ToTable("ConfigLog");
            // 控制器表（阶段 2 新增，控制器/PLC 资产台账）：
            // 编码唯一（Code 为业务键）；类型落地为 ProtocolId FK → Protocols（Restrict，
            // 协议被控制器引用后不可删除，与 DataModel 绑定协议的删除约束行为一致）。
            modelBuilder.Entity<Controller>().ToTable("Controllers");
            modelBuilder.Entity<Controller>()
                .Property(c => c.Code).HasMaxLength(50);
            modelBuilder.Entity<Controller>()
                .Property(c => c.Name).HasMaxLength(100);
            modelBuilder.Entity<Controller>()
                .Property(c => c.Manufacturer).HasMaxLength(100);
            modelBuilder.Entity<Controller>()
                .Property(c => c.Model).HasMaxLength(100);
            modelBuilder.Entity<Controller>()
                .Property(c => c.Description).HasMaxLength(500);
            modelBuilder.Entity<Controller>()
                .HasIndex(c => c.Code)
                .IsUnique()
                .HasDatabaseName("ix_controllers_code");
            modelBuilder.Entity<Controller>()
                .HasOne(c => c.Protocol)
                .WithMany()
                .HasForeignKey(c => c.ProtocolId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Controllers_Protocols_ProtocolId");
            // 设备连接表（阶段 3 新增）：连接参数从 Device.JsonConfig 抽取为独立实体。
            // FK 均 Restrict——控制器/协议被连接引用后不可删除，杜绝静默级联。
            modelBuilder.Entity<DeviceConnection>().ToTable("DeviceConnections");
            modelBuilder.Entity<DeviceConnection>()
                .Property(c => c.Name).HasMaxLength(100);
            modelBuilder.Entity<DeviceConnection>()
                .Property(c => c.Host).HasMaxLength(100);
            modelBuilder.Entity<DeviceConnection>()
                .Property(c => c.ConfigJson).HasColumnType("longtext");
            modelBuilder.Entity<DeviceConnection>()
                .HasIndex(c => c.ControllerId)
                .HasDatabaseName("ix_deviceconnections_controllerid");
            modelBuilder.Entity<DeviceConnection>()
                .HasOne(c => c.Controller)
                .WithMany(c => c.Connections)
                .HasForeignKey(c => c.ControllerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_DeviceConnections_Controllers_ControllerId");
            modelBuilder.Entity<DeviceConnection>()
                .HasOne(c => c.Protocol)
                .WithMany()
                .HasForeignKey(c => c.ProtocolId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_DeviceConnections_Protocols_ProtocolId");
            // 设备-数据模型绑定表（阶段 5 新增，多对多中间表）：
            // (DeviceId, DataModelId) 库级唯一索引防重复绑定；删设备 Cascade 自动清绑定行；
            // 删模型 Restrict（被绑定模型不可删除）；Version 显式限长映射 varchar(20)。
            // 「一台设备至多一条 IsPrimary=true」由应用层在事务内校验 + 降级旧主维护（MVP 不做库级部分唯一索引）。
            modelBuilder.Entity<DeviceDataModel>().ToTable("DeviceDataModels");
            modelBuilder.Entity<DeviceDataModel>()
                .Property(b => b.Version).HasMaxLength(20);
            modelBuilder.Entity<DeviceDataModel>()
                .HasIndex(b => new { b.DeviceId, b.DataModelId })
                .IsUnique()
                .HasDatabaseName("ix_devicedatamodels_device_model");
            modelBuilder.Entity<DeviceDataModel>()
                .HasIndex(b => b.DeviceId)
                .HasDatabaseName("ix_devicedatamodels_deviceid");
            modelBuilder.Entity<DeviceDataModel>()
                .HasOne(b => b.Device)
                .WithMany(d => d.DeviceDataModels)
                .HasForeignKey(b => b.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DeviceDataModel>()
                .HasOne(b => b.DataModel)
                .WithMany()
                .HasForeignKey(b => b.DataModelId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_DeviceDataModels_DataModels_DataModelId");
            modelBuilder.Entity<DatabaseConfig>().ToTable("DatabaseConfigs");
            modelBuilder.Entity<DataConversion>().ToTable("DataConversions");
            // 数据模型（阶段 4 补全 Code/Version）：Code/Version 显式限长映射为 varchar
            // （Pomelo 对无长度 string 默认映射 longtext，无法建索引）；Code 唯一索引见 AddDataModelCodeUniqueIndex 迁移。
            modelBuilder.Entity<DataModel>().ToTable("DataModels");
            modelBuilder.Entity<DataModel>()
                .Property(dm => dm.Code).HasMaxLength(100);
            modelBuilder.Entity<DataModel>()
                .Property(dm => dm.Version).HasMaxLength(20);
            // 模型编码唯一（业务键，阶段 4）：NULL 允许多条共存（存量回填已完成后再建索引）。
            modelBuilder.Entity<DataModel>()
                .HasIndex(dm => dm.Code)
                .IsUnique()
                .HasDatabaseName("ix_datamodels_code");
            modelBuilder.Entity<DbVersion>().ToTable("DbVersion");
            modelBuilder.Entity<Device>().ToTable("Devices");
            modelBuilder.Entity<ExposedInterface>().ToTable("ExposedInterfaces");
            // 开放接口：路由与请求方法需建库级唯一索引兜底（并发/直写库时的数据库级约束）。
            // 列需显式限制长度映射为 varchar（Pomelo 对无长度 string 默认映射 longtext，无法建索引），
            // 与实体上的 [MaxLength] 保持一致。
            modelBuilder.Entity<ExposedInterface>()
                .Property(x => x.Name).HasMaxLength(100);
            modelBuilder.Entity<ExposedInterface>()
                .Property(x => x.RouteUrl).HasMaxLength(512);
            modelBuilder.Entity<ExposedInterface>()
                .Property(x => x.RequestMethod).HasMaxLength(16);
            modelBuilder.Entity<ExposedInterface>()
                .Property(x => x.ExposedKey).HasMaxLength(256);
            modelBuilder.Entity<ExposedInterface>()
                .HasIndex(x => new { x.RouteUrl, x.RequestMethod })
                .IsUnique()
                .HasDatabaseName("ix_exposedinterfaces_route_method");
            modelBuilder.Entity<HmiComponent>().ToTable("HmiComponents");
            // 模型变量：列宽显式限制为 varchar（Pomelo 对无长度 string 默认映射 longtext，无法建索引），
            // 并建立 (ModelId, Key) 库级唯一索引兜底，杜绝并发/直写库造成"模型内变量键重复"。
            modelBuilder.Entity<DataPoint>().ToTable("DataPoints");
            modelBuilder.Entity<DataPoint>()
                .Property(m => m.Key).HasMaxLength(50);
            modelBuilder.Entity<DataPoint>()
                .Property(m => m.Name).HasMaxLength(50);
            modelBuilder.Entity<DataPoint>()
                .Property(m => m.Unit).HasMaxLength(32);
            modelBuilder.Entity<DataPoint>()
                .Property(m => m.Description).HasMaxLength(500);
            // 工程换算表达式：显式限长，与实体 [MaxLength(200)] 及应用层校验保持一致
            modelBuilder.Entity<DataPoint>()
                .Property(m => m.ScaleExpression).HasMaxLength(200);
            // 读写模式（阶段 4）：显式限长映射为 varchar(16)，与实体 [MaxLength(16)] 一致
            modelBuilder.Entity<DataPoint>()
                .Property(m => m.AccessMode).HasMaxLength(16);
            modelBuilder.Entity<DataPoint>()
                .HasIndex(m => new { m.ModelId, m.Key })
                .IsUnique()
                .HasDatabaseName("ix_modelvariable_model_key");
            // 模型变量归属数据模型：显式 Restrict，删除模型前由应用层显式清理变量，杜绝孤儿或静默级联。
            modelBuilder.Entity<DataPoint>()
                .HasOne<DataModel>()
                .WithMany()
                .HasForeignKey(m => m.ModelId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_DataPoints_DataModels_ModelId");

            modelBuilder.Entity<Protocol>().ToTable("Protocols");
            modelBuilder.Entity<Protocol>()
                .HasIndex(p => p.Key)
                .IsUnique()
                .HasDatabaseName("ix_protocol_key");

            // 协议 <-> 数据模型关系（EF Core Fluent API 显式配置，符合最佳实践）。
            // DataModel.ProtocolId 为必填外键（模型必须绑定协议，作为驱动派发真相源）；
            // 删除协议时采用 Restrict，避免级联误删已关联的数据模型。
            modelBuilder.Entity<DataModel>()
                .HasOne(dm => dm.Protocol)
                .WithMany()
                .HasForeignKey(dm => dm.ProtocolId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_DataModels_Protocols_ProtocolId");
            modelBuilder.Entity<MqttServer>().ToTable("MqttServers");
            modelBuilder.Entity<MqttVariableConfig>().ToTable("MqttVariableConfigs");
            // MQTT 变量映射唯一约束：同一服务器下同一设备同一变量仅能关联一次，
            // 从数据库层面防重复关联（别名可不同服务器各自独立）。
            modelBuilder.Entity<MqttVariableConfig>()
                .HasIndex(m => new { m.MqttServerId, m.DeviceId, m.VariableKey })
                .IsUnique()
                .HasDatabaseName("ix_mqttvariableconfig_server_device_var");
            modelBuilder.Entity<ScadaPage>().ToTable("ScadaPages");
            modelBuilder.Entity<ScadaProject>().ToTable("ScadaProjects");
            modelBuilder.Entity<ScheduledTask>().ToTable("ScheduledTasks");
            modelBuilder.Entity<Sensor>().ToTable("Sensors");
            modelBuilder.Entity<DataPointMapping>().ToTable("DataPointMappings");
            // 工程换算表达式覆盖值：显式限长，与实体 [MaxLength(200)] 保持一致
            modelBuilder.Entity<DataPointMapping>()
                .Property(dv => dv.ScaleExpressionOverride).HasMaxLength(200);
            // 原始数据类型字符串形式（阶段 4）：显式限长映射为 varchar(32)，与实体 [MaxLength(32)] 一致
            modelBuilder.Entity<DataPointMapping>()
                .Property(dv => dv.RawDataType).HasMaxLength(32);
            modelBuilder.Entity<DataPointMapping>()
                .HasIndex(dv => new { dv.DeviceId, dv.DataPointId })
                .IsUnique()
                .HasDatabaseName("ix_devicevariable_device_model");
            modelBuilder.Entity<SystemConfig>().ToTable("SystemConfig");
            modelBuilder.Entity<SystemScript>().ToTable("SystemScripts");

            // 脚本执行记录表：索引支撑「按脚本控制台追溯」与「按时间清理」。
            // 数据量大，不建外键，避免级联删除/迁移开销影响运行时写入性能（同 AlarmRecord/VariableHistory 设计）。
            modelBuilder.Entity<ScriptExecutionRecord>().ToTable("ScriptExecutionRecords");
            modelBuilder.Entity<ScriptExecutionRecord>()
                .HasIndex(r => new { r.ScriptId, r.StartedAt })
                .HasDatabaseName("ix_scriptexecrecord_script_started");

            // 系统日志表：统一承载运行/操作/安全日志。
            // 需索引或精确匹配的列显式限制长度映射为 varchar（Pomelo 对无长度 string 默认映射 longtext，无法建索引）；
            // Content/Operator 为自由文本，Content 保持 longtext。
            modelBuilder.Entity<SystemLog>().ToTable("SystemLogs");
            modelBuilder.Entity<SystemLog>()
                .Property(l => l.Category).HasMaxLength(16);
            modelBuilder.Entity<SystemLog>()
                .Property(l => l.Level).HasMaxLength(16);
            modelBuilder.Entity<SystemLog>()
                .Property(l => l.Source).HasMaxLength(64);
            modelBuilder.Entity<SystemLog>()
                .Property(l => l.Operation).HasMaxLength(16);
            modelBuilder.Entity<SystemLog>()
                .Property(l => l.Operator).HasMaxLength(64);
            modelBuilder.Entity<SystemLog>()
                .Property(l => l.IpAddress).HasMaxLength(45);
            modelBuilder.Entity<SystemLog>()
                .Property(l => l.RelatedId).HasMaxLength(64);
            modelBuilder.Entity<SystemLog>()
                .Property(l => l.Content).HasColumnType("longtext");

            // 日志查询主索引：分类 + 时间（支撑「分类 Tab + 时间段 + 关键字」组合查询，先按索引收窄再 LIKE）；
            // 独立时间索引供「全部分类 + 时间段」与清理任务使用。
            modelBuilder.Entity<SystemLog>()
                .HasIndex(l => new { l.Category, l.Timestamp })
                .HasDatabaseName("ix_systemlog_category_timestamp");
            modelBuilder.Entity<SystemLog>()
                .HasIndex(l => l.Timestamp)
                .HasDatabaseName("ix_systemlog_timestamp");

            modelBuilder.Entity<SystemUser>().ToTable("SystemUsers");
            // 用户名唯一（P0 修复：收窄列宽为 varchar(64) 以支撑唯一索引，杜绝重名登录歧义）
            modelBuilder.Entity<SystemUser>()
                .Property(u => u.Username)
                .HasMaxLength(64);
            modelBuilder.Entity<SystemUser>()
                .HasIndex(u => u.Username)
                .IsUnique()
                .HasDatabaseName("ix_systemusers_username");

            // 变量历史数据表：按 变量键 + 时间 建复合索引，支撑历史趋势查询。
            // 历史数据量大，暂不建外键，避免级联删除/迁移开销影响运行时写入性能。
            modelBuilder.Entity<VariableHistory>().ToTable("VariableHistory");
            modelBuilder.Entity<VariableHistory>()
                .HasIndex(h => new { h.VariableKey, h.Timestamp })
                .HasDatabaseName("ix_variablehistory_key_timestamp");

            // 变量实时快照表：每设备每变量一行，复合主键 (DeviceId, VariableKey)，
            // 由实时快照服务批量 Upsert，无需自增主键。
            modelBuilder.Entity<VariableRealtime>().ToTable("VariableRealtime");
            modelBuilder.Entity<VariableRealtime>()
                .HasKey(r => new { r.DeviceId, r.VariableKey });
            modelBuilder.Entity<VariableRealtime>()
                .HasIndex(r => new { r.DeviceKey, r.VariableKey })
                .HasDatabaseName("ix_variablerealtime_devicekey_variablekey");

            // Devices.Key 唯一索引
            modelBuilder.Entity<Device>()
                .HasIndex(d => d.Key)
                .IsUnique()
                .HasDatabaseName("ix_device_key");

            // 导航关系（EF 通过外键标量属性推断，复杂关系显式配置）
            modelBuilder.Entity<Device>()
                .HasOne(d => d.Area)
                .WithMany()
                .HasForeignKey(d => d.AreaId);

            modelBuilder.Entity<Device>()
                .HasOne(d => d.Model)
                .WithMany()
                .HasForeignKey(d => d.ModelId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Devices_DataModels_ModelId");

            // 阶段 3 新增：设备 → 控制器 / 设备连接（可空 FK，Restrict）。
            // 连接实体被设备引用后不可删除（与 HmiComponent.BindDeviceId SetNull 不同：此处为结构归属）。
            modelBuilder.Entity<Device>()
                .HasOne(d => d.Controller)
                .WithMany()
                .HasForeignKey(d => d.ControllerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Devices_Controllers_ControllerId");

            modelBuilder.Entity<Device>()
                .HasOne(d => d.Connection)
                .WithMany()
                .HasForeignKey(d => d.ConnectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Devices_DeviceConnections_ConnectionId");

            modelBuilder.Entity<ExposedInterface>()
                .HasOne(x => x.Device)
                .WithMany()
                .HasForeignKey(x => x.DeviceId);

            modelBuilder.Entity<ScadaPage>()
                .HasOne(p => p.Project)
                .WithMany()
                .HasForeignKey(p => p.ProjectId);

            // HMI 组件绑定设备：BindDeviceId 为可空外键，设备删除时绑定置 NULL（画面组件保留，仅提示绑定失效）
            modelBuilder.Entity<HmiComponent>()
                .HasOne<Device>()
                .WithMany()
                .HasForeignKey(c => c.BindDeviceId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_HmiComponents_Devices_BindDeviceId");

            // 组件归属图层 ID（前端 uid 引用，最长约 30 字符，取 64 上限；无查询需求，不建索引）
            modelBuilder.Entity<HmiComponent>()
                .Property(c => c.LayerId)
                .HasMaxLength(64);

            // 页面画布尺寸默认值（与前端硬编码 1100×700 对齐）
            modelBuilder.Entity<ScadaPage>()
                .Property(p => p.Width)
                .HasDefaultValue(1100);

            modelBuilder.Entity<ScadaPage>()
                .Property(p => p.Height)
                .HasDefaultValue(700);

            // 画面归属端（桌面/移动），默认 Desktop；运行态会高频按 (ProjectId, Platform) 过滤
            modelBuilder.Entity<ScadaPage>()
                .Property(p => p.Platform)
                .HasMaxLength(16)
                .HasDefaultValue("Desktop");

            // 页面背景配置 JSON（长文本）与运行端自适应模式（可空，NULL=未配置回退默认）
            modelBuilder.Entity<ScadaPage>()
                .Property(p => p.BackgroundJson)
                .HasColumnType("longtext");

            // 页面图层配置 JSON（长文本，结构由前端负责，后端透传）
            modelBuilder.Entity<ScadaPage>()
                .Property(p => p.LayersJson)
                .HasColumnType("longtext");

            modelBuilder.Entity<ScadaPage>()
                .Property(p => p.AdaptMode)
                .HasMaxLength(32);

            modelBuilder.Entity<ScadaPage>()
                .HasIndex(p => new { p.ProjectId, p.Platform })
                .HasDatabaseName("IX_ScadaPages_ProjectId_Platform");

            modelBuilder.Entity<Sensor>()
                .HasOne(s => s.Device)
                .WithMany()
                .HasForeignKey(s => s.DeviceId);

            modelBuilder.Entity<DataPointMapping>()
                .HasOne(dv => dv.Device)
                .WithMany(d => d.DataPointMappings)
                .HasForeignKey(dv => dv.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DataPointMapping>()
                .HasOne(dv => dv.DataPoint)
                .WithMany(mv => mv.DataPointMappings)
                .HasForeignKey(dv => dv.DataPointId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_DataPointMappings_DataPoints_DataPointId");

            // 阶段 4 新增：设备变量 → 连接（可空 FK，Restrict，语义同 Device.ConnectionId——变量级覆盖，
            // 连接被引用后不可删除）。索引支撑「按连接检索变量」。
            modelBuilder.Entity<DataPointMapping>()
                .HasOne<DeviceConnection>()
                .WithMany()
                .HasForeignKey(dv => dv.ConnectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_DataPointMappings_DeviceConnections_ConnectionId");
            modelBuilder.Entity<DataPointMapping>()
                .HasIndex(dv => dv.ConnectionId)
                .HasDatabaseName("ix_devicevariables_connectionid");

            // 长文本列类型（MySQL 不支持 nvarchar(max)/text 默认映射，显式指定）
            modelBuilder.Entity<HmiComponent>()
                .Property(c => c.PropsJson)
                .HasColumnType("longtext");

            modelBuilder.Entity<DataPoint>()
                .Property(m => m.ExtensionData)
                .HasConversion(ExtensionDataConverter, ExtensionDataComparer)
                .HasColumnType("longtext");

            modelBuilder.Entity<DataPointMapping>()
                .Property(dv => dv.ExtensionData)
                .HasConversion(ExtensionDataConverter, ExtensionDataComparer)
                .HasColumnType("longtext");

            modelBuilder.Entity<ScheduledTask>()
                .Property(t => t.ParamsJson)
                .HasColumnType("longtext");

            modelBuilder.Entity<SystemScript>()
                .Property(s => s.Code)
                .HasColumnType("longtext");
        }
    }
}
