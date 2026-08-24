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
            modelBuilder.Entity<MqttServer>().ToTable("MqttServers");
            modelBuilder.Entity<MqttVariableConfig>().ToTable("MqttVariableConfigs");
            modelBuilder.Entity<ScadaPage>().ToTable("ScadaPages");
            modelBuilder.Entity<ScadaProject>().ToTable("ScadaProjects");
            modelBuilder.Entity<ScheduledTask>().ToTable("ScheduledTasks");
            modelBuilder.Entity<Sensor>().ToTable("Sensors");
            modelBuilder.Entity<SystemConfig>().ToTable("SystemConfig");
            modelBuilder.Entity<SystemLog>().ToTable("SystemLogs");
            modelBuilder.Entity<SystemScript>().ToTable("SystemScripts");
            modelBuilder.Entity<SystemUser>().ToTable("SystemUsers");
            modelBuilder.Entity<VariableTrigger>().ToTable("VariableTriggers");

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

            modelBuilder.Entity<Sensor>()
                .HasOne(s => s.Device)
                .WithMany()
                .HasForeignKey(s => s.DeviceId);

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

            modelBuilder.Entity<ScheduledTask>()
                .Property(t => t.ParamsJson)
                .HasColumnType("longtext");

            modelBuilder.Entity<SystemScript>()
                .Property(s => s.Code)
                .HasColumnType("longtext");
        }
    }
}
