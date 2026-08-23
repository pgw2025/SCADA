# SCADA Server：SqlSugar → EF Core 迁移方案

> 状态：待评审（用户已确认需要此文档，执行前需最终拍板）
> 目标框架：.NET 8.0
> 当前 ORM：`SqlSugarCore 5.1.4.214`（SqlSugarScope + Repository 模式）
> 目标数据库：MySQL（保持不动）

---

## 1. 现状盘点（已代码核实）

### 1.1 分层结构
```
Server/
├── ScadaServer.Domain/          # 实体 + 仓储/工作单元接口（不含 ORM 依赖）
│   ├── Entities/                # 23 个实体，全部继承 EntityBase
│   └── Interfaces/Repositories/ # IRepository<T, TKey>
├── ScadaServer.Application/     # 业务用例，仅依赖 IRepository / IUnitOfWork
├── ScadaServer.Infrastructure/  # SqlSugar 实现（变更集中区）
│   ├── Persistence/             # SqlSugarUnitOfWork.cs, DatabaseInitializer.cs
│   └── Repositories/            # RepositoryBase.cs + 21 个空壳实现
└── ScadaServer.WebApi/          # 仅 Database.Extensions.cs / Program.cs 知道 SqlSugar
```

### 1.2 关键事实
- **23 张表**，全部继承 `EntityBase`（`Id` 自增主键）。
- 实体标注：`[SugarTable]`×23、`[SugarColumn]`×23、`[SugarIndex]`×1（Devices.Key 唯一索引）、`[Navigate]`×14（导航属性）。
- 21 个 Repository 实现类**全部为空壳**（`class XRepository : RepositoryBase<...>, IXRepository`），无自定义方法 —— 改写基类即可全量覆盖。
- **Application 层不直接引用 SqlSugar**，仅通过 `IRepository<T,TKey>` 与 `IUnitOfWork` 接口访问数据 —— 业务代码一行不改。
- 直接依赖 SqlSugar 的文件仅 3 类：
  - `ScadaServer.Infrastructure/Persistence/SqlSugarUnitOfWork.cs`
  - `ScadaServer.Infrastructure/Persistence/DatabaseInitializer.cs`
  - `ScadaServer.Infrastructure/Repositories/RepositoryBase.cs`（及 21 个继承它的 Repository）
  - `ScadaServer.WebApi/Extensions/Database.Extensions.cs`（DI 注册）
  - `ScadaServer.WebApi/Program.cs`（调用 `AddDatabaseServices()`）

### 1.3 结论
架构分层良好，本次迁移属于**「换引擎不换车厢」**：改动严格收敛在 `Infrastructure` + `WebApi` 两个工程，Application / Domain 接口 / 实体业务语义保持不变。

---

## 2. 推荐方案总览

采用 **EF Core 8（Code First）+ MySQL（Pomelo 提供程序）**，分 7 步实施：

| 步骤 | 改什么 | 影响层 |
|---|---|---|
| 2.1 | 实体标注 Sugar→EF 特性 | Domain |
| 2.2 | 新增 `ScadaDbContext` | Infrastructure |
| 2.3 | 改写 `RepositoryBase` | Infrastructure |
| 2.4 | 改写 `SqlSugarUnitOfWork` → EF | Infrastructure |
| 2.5 | 改写 `DatabaseInitializer`（迁移/种子） | Infrastructure |
| 2.6 | 改 DI 注册 + 卸载 SqlSugar 包 | WebApi + Infra |
| 2.7 | 编译 + 测试库验证 | 全量 |

---

## 3. 详细改造步骤

### 3.1 实体映射改造（Domain/Entities）
逐个文件移除 `using SqlSugar;`，按对照表改写特性：

| SqlSugar | EF Core 等效 |
|---|---|
| `[SugarTable("Devices")]` | `[Table("Devices")]`（需 `using System.ComponentModel.DataAnnotations.Schema;`） |
| `[SugarColumn(IsPrimaryKey=true, IsIdentity=true)]` | `[Key]` + `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]` |
| `[SugarColumn(Length=100, IsNullable=false)]` | `[MaxLength(100)]` + `[Required]`（或保持 `string` 非可空） |
| `[SugarColumn(IsNullable=true)]` / `DateTime?` | 保持 `?` 可空类型即可（EF 自动映射 NULL） |
| `[SugarIndex("ix_device_key", nameof(Key), OrderByType.Asc, true)]` | 移到 `ScadaDbContext.OnModelCreating`：`modelBuilder.Entity<Device>().HasIndex(d => d.Key).IsUnique().HasDatabaseName("ix_device_key");` |
| `[Navigate(NavigateType.OneToOne/OneToMany, ...)]` | **直接删除特性**，保留导航属性；EF 通过外键标量属性（如 `AreaId`）自动推断关系 |
| `[SugarColumn(IsIgnore=true)]` ×3 | `[NotMapped]` |

`EntityBase.cs` 改造示例：
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScadaServer.Domain.Entities;

public abstract class EntityBase
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
}
```

### 3.2 新增 ScadaDbContext（Infrastructure/Persistence）
```csharp
using Microsoft.EntityFrameworkCore;
using ScadaServer.Domain.Entities;

namespace ScadaServer.Infrastructure.Persistence;

public class ScadaDbContext : DbContext
{
    public ScadaDbContext(DbContextOptions<ScadaDbContext> options) : base(options) { }

    // 每个实体一个 DbSet（与现有 23 表一一对应）
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Area> Areas => Set<Area>();
    // ... 其余 21 个

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 表名（若类名与表名一致可省略，但现有表名显式声明更安全）
        modelBuilder.Entity<Device>().ToTable("Devices");
        // 唯一索引（原 SugarIndex）
        modelBuilder.Entity<Device>()
            .HasIndex(d => d.Key)
            .IsUnique()
            .HasDatabaseName("ix_device_key");
        // 关系（原 Navigate）由外键属性自动推断；复杂关系在此显式配置
        modelBuilder.Entity<Device>()
            .HasOne(d => d.Area)
            .WithMany()
            .HasForeignKey(d => d.AreaId);
        // ... 其余导航关系
    }
}
```

### 3.3 改写 RepositoryBase（核心改动点）
`ISqlSugarClient` → `ScadaDbContext`，方法改写：
```csharp
public abstract class RepositoryBase<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, new()
{
    protected readonly ScadaDbContext Db;
    protected RepositoryBase(ScadaDbContext db) => Db = db;

    public virtual async Task<TEntity?> GetByIdAsync(TKey id)
        => await Db.Set<TEntity>().FindAsync(id);

    public virtual async Task<List<TEntity>> GetListAsync()
        => await Db.Set<TEntity>().ToListAsync();

    public virtual async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity,bool>> p)
        => await Db.Set<TEntity>().Where(p).ToListAsync();

    public virtual async Task<List<TEntity>> GetPagedListAsync(int page, int size, Expression<Func<TEntity,bool>>? p = null)
    {
        var q = Db.Set<TEntity>().AsQueryable();
        if (p != null) q = q.Where(p);
        return await q.OrderBy(e => EF.Property<int>(e, "Id")).Skip((page-1)*size).Take(size).ToListAsync();
    }

    public virtual async Task InsertAsync(TEntity e)
    { Db.Set<TEntity>().Add(e); await Db.SaveChangesAsync(); }

    public virtual async Task InsertRangeAsync(IEnumerable<TEntity> es)
    { Db.Set<TEntity>().AddRange(es); await Db.SaveChangesAsync(); }

    public virtual async Task UpdateAsync(TEntity e)
    { Db.Set<TEntity>().Update(e); await Db.SaveChangesAsync(); }

    public virtual async Task DeleteAsync(TKey id)
    { var e = await GetByIdAsync(id); if (e != null) { Db.Set<TEntity>().Remove(e); await Db.SaveChangesAsync(); } }
    // DeleteAsync(entity) / DeleteRangeAsync(predicate) 类似
}
```
> 21 个空壳 Repository 的构造函数参数从 `ISqlSugarClient` 改为 `ScadaDbContext`（仅改签名，逻辑不动）。

### 3.4 改写 UnitOfWork
- `SqlSugarUnitOfWork` 重命名为 `EfUnitOfWork`（或就地改写），基于 `DbContext.Database`：
  ```csharp
  public class EfUnitOfWork : IUnitOfWork
  {
      private readonly ScadaDbContext _db;
      public EfUnitOfWork(ScadaDbContext db) => _db = db;
      public void BeginTran() => _db.Database.BeginTransaction();
      public async Task CommitTranAsync() => await _db.Database.CommitTransactionAsync();
      public async Task RollbackTranAsync() => await _db.Database.RollbackTransactionAsync();
      public async Task<ITransactionScope> BeginTransactionAsync()
      {
          var tx = await _db.Database.BeginTransactionAsync();
          return new EfTransactionScope(tx);
      }
      // ITransactionScope 包装 IDbContextTransaction
  }
  ```
- `IUnitOfWork` / `ITransactionScope` 接口**保持不变**，上层调用无感。

### 3.5 改写 DatabaseInitializer
- 原 `CreateTables()`（SqlSugar `CodeFirst.InitTables`）→ 改为 EF 迁移：
  - **推荐**：`await _db.Database.MigrateAsync();`（执行已生成的 Migration，具备版本历史与回滚能力）。
  - 种子数据（`CreateDefaultAreaAsync` / `CreateDefaultAdminAsync` / `SaveDbVersionAsync`）逻辑原样保留，仅把 `Queryable<T>().AnyAsync()` 等改为 EF 写法（`Db.Set<T>().AnyAsync()`）。
- **存量库兼容**：原 `EnsureDeviceKeyUniqueIndex()` / `EnsureDeviceColumnsNullable()` 这两段针对老 MySQL 库的补丁，迁移后在对应 Migration 的 `Up()` 中用 `migrationBuilder.CreateIndex(...)` / `migrationBuilder.AlterColumn(...)` 重做，保证现场升级不丢数据、不报错。

### 3.6 DI 注册 + 卸载包（WebApi / Infra）
`Database.Extensions.cs` 的 `AddDatabaseServices` 改写：
```csharp
services.AddDbContext<ScadaDbContext>(opt =>
    opt.UseMySql(options.GetConnectionString(),
        ServerVersion.AutoDetect(options.GetConnectionString())));
services.AddScoped<IUnitOfWork, EfUnitOfWork>();
// 21 个 Repository 接口映射保持原样，仅实现类换构造参数
services.AddScoped<IDeviceRepository, DeviceRepository>();
// ...
```
- `ScadaServer.Infrastructure.csproj`：移除 `<PackageReference Include="SqlSugarCore" ... />`，新增：
  - `Microsoft.EntityFrameworkCore 8.x`
  - `Pomelo.EntityFrameworkCore.MySql 8.x`
  - （如需命令行迁移）`Microsoft.EntityFrameworkCore.Tools`
- `Program.cs` 中 `builder.Services.AddDatabaseServices();` 保持不变。

---

## 4. 待用户拍板的两个决策点

> 执行前必须确认，影响 3.5 / 3.2 的实现方式。

1. **迁移策略**
   - **A. EF Migrations（推荐）**：生成可版本化、可回滚的迁移历史，适合长期维护与多环境部署。需额外生成 `Migrations/` 目录。
   - **B. EnsureCreated + 手写建表**：启动即按模型建库，简单无历史，适合纯新项目 / 演示。

2. **存量数据库**
   - 现场已有 MySQL 数据需保留 → 用 Migration **增量升级**（不删表、不丢数据）。
   - 仅全新库 → 可直接 Code First 重建。

---

## 5. 执行顺序与验证

1. 改 csproj 包引用 → 2. 改实体特性 → 3. 写 ScadaDbContext → 4. 改写 RepositoryBase + 21 构造签名 → 5. 改写 UoW → 6. 改写 Initializer（含首次 Migration）→ 7. 改 DI → 8. 卸载 SqlSugar → 9. `dotnet build` → 10. 测试库跑 Migrate + 种子 + 一条读/写链路。

**完成判据**：`dotnet build` 零错误；测试 MySQL 实例上 `MigrateAsync` 成功、种子数据落库、通过 Repository 可读写。

---

## 6. 风险与对策
| 风险 | 对策 |
|---|---|
| 导航关系推断错误导致外键缺失 | 3.2 中显式 `HasOne/WithMany/HasForeignKey` 配置全部关系 |
| 老库 `LastCommunicationTime` NOT NULL 冲突 | Migration `Up()` 中 `AlterColumn` 改为可空（沿用原逻辑） |
| 分页排序字段不一致 | 统一按 `Id` 排序（见 3.3），如需业务排序后续扩展 |
| EF 与 SqlSugar 列名/类型微差 | 全部实体显式 `ToTable`/`MaxLength`，避免约定差异 |
| 卸载 SqlSugar 后残留引用 | 改完全局 `grep -r "SqlSugar"` 应为 0 处 |
