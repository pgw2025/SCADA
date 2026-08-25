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
        public DbSet<LinkageRule> LinkageRules => Set<LinkageRule>();
        public DbSet<Area> Areas => Set<Area>();
        public DbSet<ConfigLog> ConfigLogs => Set<ConfigLog>();
        public DbSet<DatabaseConfig> DatabaseConfigs => Set<DatabaseConfig>();
        public DbSet<DataConversion> DataConversions => Set<DataConversion>();
        public DbSet<DataModel> DataModels => Set<DataModel>();
        public DbSet<DbVersion> DbVersions => Set<DbVersion>();
        public DbSet<Device> Devices => Set<Device>();
        public DbSet<DeviceConfig> DeviceConfigs => Set<DeviceConfig>();
        public DbSet<DeviceVariable> DeviceVariables => Set<DeviceVariable>();
        public DbSet<ExposedInterface> ExposedInterfaces => Set<ExposedInterface>();
        public DbSet<HmiComponent> HmiComponents => Set<HmiComponent>();
        public DbSet<ModelVariable> ModelVariables => Set<ModelVariable>();
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
        public DbSet<SystemUser> SystemUsers => Set<SystemUser>();
        public DbSet<VariableTrigger> VariableTriggers => Set<VariableTrigger>();
        public DbSet<VariableHistory> VariableHistories => Set<VariableHistory>();

        #endregion

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 表名（与现有库一致，显式声明避免 EF 复数约定差异）
            modelBuilder.Entity<AlarmRule>().ToTable("AlarmRules");
            modelBuilder.Entity<LinkageRule>().ToTable("LinkageRules");
            modelBuilder.Entity<Area>().ToTable("Areas");
            modelBuilder.Entity<ConfigLog>().ToTable("ConfigLog");
            modelBuilder.Entity<DatabaseConfig>().ToTable("DatabaseConfigs");
            modelBuilder.Entity<DataConversion>().ToTable("DataConversions");
            modelBuilder.Entity<DataModel>().ToTable("DataModels");
            modelBuilder.Entity<DbVersion>().ToTable("DbVersion");
            modelBuilder.Entity<Device>().ToTable("Devices");
            modelBuilder.Entity<DeviceConfig>().ToTable("DeviceConfigs");
            modelBuilder.Entity<ExposedInterface>().ToTable("ExposedInterfaces");
            modelBuilder.Entity<HmiComponent>().ToTable("HmiComponents");
            modelBuilder.Entity<ModelVariable>().ToTable("ModelVariables");

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
            modelBuilder.Entity<ScadaPage>().ToTable("ScadaPages");
            modelBuilder.Entity<ScadaProject>().ToTable("ScadaProjects");
            modelBuilder.Entity<ScheduledTask>().ToTable("ScheduledTasks");
            modelBuilder.Entity<Sensor>().ToTable("Sensors");
            modelBuilder.Entity<DeviceVariable>().ToTable("DeviceVariables");
            modelBuilder.Entity<DeviceVariable>()
                .HasIndex(dv => new { dv.DeviceId, dv.ModelVariableId })
                .IsUnique()
                .HasDatabaseName("ix_devicevariable_device_model");
            modelBuilder.Entity<SystemConfig>().ToTable("SystemConfig");
            modelBuilder.Entity<SystemLog>().ToTable("SystemLogs");
            modelBuilder.Entity<SystemScript>().ToTable("SystemScripts");
            modelBuilder.Entity<SystemUser>().ToTable("SystemUsers");
            modelBuilder.Entity<VariableTrigger>().ToTable("VariableTriggers");

            // 变量历史数据表：按 变量键 + 时间 建复合索引，支撑历史趋势查询。
            // 历史数据量大，暂不建外键，避免级联删除/迁移开销影响运行时写入性能。
            modelBuilder.Entity<VariableHistory>().ToTable("VariableHistory");
            modelBuilder.Entity<VariableHistory>()
                .HasIndex(h => new { h.VariableKey, h.Timestamp })
                .HasDatabaseName("ix_variablehistory_key_timestamp");

            // 主键（DeviceConfig 使用 DeviceId 作为主键，非自增）
            modelBuilder.Entity<DeviceConfig>()
                .HasKey(d => d.DeviceId);

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
                .HasForeignKey(d => d.ModelId);

            modelBuilder.Entity<Device>()
                .HasOne(d => d.Config)
                .WithOne()
                .HasForeignKey<DeviceConfig>(c => c.DeviceId);

            modelBuilder.Entity<Device>()
                .HasMany(d => d.Triggers)
                .WithOne()
                .HasForeignKey(nameof(VariableTrigger.DeviceId));

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

            modelBuilder.Entity<ScadaPage>()
                .HasIndex(p => new { p.ProjectId, p.Platform })
                .HasDatabaseName("IX_ScadaPages_ProjectId_Platform");

            modelBuilder.Entity<Sensor>()
                .HasOne(s => s.Device)
                .WithMany()
                .HasForeignKey(s => s.DeviceId);

            modelBuilder.Entity<DeviceVariable>()
                .HasOne(dv => dv.Device)
                .WithMany(d => d.DeviceVariables)
                .HasForeignKey(dv => dv.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DeviceVariable>()
                .HasOne(dv => dv.ModelVariable)
                .WithMany(mv => mv.DeviceVariables)
                .HasForeignKey(dv => dv.ModelVariableId)
                .OnDelete(DeleteBehavior.Cascade);

            // 长文本列类型（MySQL 不支持 nvarchar(max)/text 默认映射，显式指定）
            modelBuilder.Entity<DeviceConfig>()
                .Property(c => c.JsonConfig)
                .HasColumnType("longtext");

            modelBuilder.Entity<HmiComponent>()
                .Property(c => c.PropsJson)
                .HasColumnType("longtext");

            modelBuilder.Entity<ModelVariable>()
                .Property(m => m.ExtensionData)
                .HasConversion(ExtensionDataConverter, ExtensionDataComparer)
                .HasColumnType("longtext");

            modelBuilder.Entity<DeviceVariable>()
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
