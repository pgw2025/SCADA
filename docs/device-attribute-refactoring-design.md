# 设备属性设计重构方案（设计文档 + 迁移计划）

- **状态**：部分实施中（A 方案已完成；D1/D2/D4 待执行）
- **日期**：2026-08-23
- **范围**：服务端 `ScadaServer` 设备/变量/配置/触发器相关领域模型与数据表
- **目标**：消除"同一概念多处编码且可漂移"的隐患，字符串魔法值枚举化，导出/驱动可扩展，并给出可灰度、可回滚的迁移路径

---

## 0. 一页速览（Before / After）

| # | 问题 | 现状（Before） | 目标（After） | 严重度 |
|---|------|----------------|---------------|--------|
| 1 | 协议真相源重复 | `Device.Type` + `Device.DriverName`(派生可覆盖) + `DataModel.Type` 三处编码协议；工厂有 `CreateDriver(DeviceType)` 与 `CreateDriver(string)` 两条路径 | 仅 `Device.Type` 为协议唯一真相；删除 `DriverName`；工厂只保留 `CreateDriver(DeviceType)`；`DataModel.Type` 移除 | P0 |
| 2 | 信号类型矛盾 | `VariableType`(Analog/Digital) 与 `DataTypeEnum` 独立存储，可矛盾 | 删除 `VariableType`，由 `DataType` 推导（`BIT/BOOL`→Digital，其余→Analog） | P0 |
| 3 | 存储策略不清 | `IsStored`(bool) + `StoreMode`(string) 关系不明、无约束 | 统一为枚举 `StoreMode { None, Raw, Compressed, Aggregated }` | P0 |
| 4 | 触发器魔法字符串 + 概念混装 | `Condition`/`ActionType`/`AlarmLevel` 为 `string`；报警与联动同表混存；`LinkageValue` 为 `string` | 拆分为 `AlarmRule` 与 `LinkageRule`；条件/级别枚举化；联动值按目标变量类型校验 | P0 |
| 5 | Key 无唯一约束 | 仅应用层 `GetListAsync` 校验，存在竞态+全表扫描 | `[SugarColumn(IsUnique=true)]` + 唯一索引；运行时走内存字典 | P1 |
| 6 | 轮询字段命名/优先级 | `Device.PollingInterval` 与 `ModelVariable.PollingIntervalMs` 同为 ms 却命名不一、优先级未定义 | 统一命名 `PollingIntervalMs`；明确"点位级覆盖设备级" | P1 |
| 7 | 配置版本/校验缺失 | `DeviceConfig.Version` 更新不自增；更新路径不校验 JSON | 更新时 `Version++`；更新路径做 JSON Schema 校验；乐观并发 | P1 |
| 8 | Min/Max 语义未定义 | 原始值还是工程值不清，与 `Threshold` 关系不明 | 明确作用于**缩放后工程值**；随单位/量程一并标注 | P1 |
| 9 | 新增协议改 4 处且未实现 | 枚举+工厂 switch+`GetDefaultDriverName`+JsonConverter；工厂仅实现 3/7 | `IDriverRegistry` 注册表替代 switch；未实现协议在**创建设备时**拦截 | P2 |
| 10 | 导出写死 MQTT | `MqttVariableConfig` 单表 | 抽象 `ExportConfig { Type, TargetKey, ConfigJson }` | P2 |
| 11 | 无软删除/审计 | 缺 `IsDeleted`/`CreatedBy`/`UpdatedBy` | `EntityBase` 增加软删除；关键实体补审计列 | P2 |
| 12 | 通信时间双写 | `Device.LastCommunicationTime` 与 `DeviceRuntime.LastCommunicationTime` 各存一份 | `Device` 侧为"最后已知"快照，运行时侧为权威 | P2 |

---

## 1. 背景与目标

当前设备属性体系分层清晰（Domain/Application/Infrastructure/Runtime/WebApi），工程属性（`ScaleSlope/Offset`、`DeadBand`、`BitOffset`、`IsReadOnly`）齐全，`DeviceConfig` 用 JSON 承载异构协议配置也是合理选择。但评审发现多处**同一概念被重复编码、且两边可能不一致**，以及用字符串代替枚举、缺少约束等问题。这些属于"隐性 bug 温床"，应在规模扩大前收敛。

目标：
1. **单一真相源**：协议、信号类型、存储策略各只有一个权威字段。
2. **类型安全**：所有有限取值改为枚举，杜绝脏数据。
3. **关注点分离**：报警与联动分表；导出目标抽象。
4. **可灰度迁移**：新字段/表先双写并存，验证后再下线旧结构。

---

## 2. 当前设计摘要（待重构部分）

| 实体 | 关键属性 | 备注 |
|------|----------|------|
| `Device` | `Name, Key, AreaId, ModelId, Type, DriverName, PollingInterval, IsEnabled, CreatedAt, UpdatedAt, LastCommunicationTime` | `DriverName` 由 `Type` 派生但可覆盖 |
| `DeviceConfig` | `DeviceId(PK), JsonConfig, Version, UpdatedAt` | `Version` 未自增 |
| `DataModel` | `Name, Description, Type, CreatedAt, UpdatedAt` | `Type` 与 `Device.Type` 强制相等（冗余） |
| `ModelVariable` | `ModelId, Key, Name, Type(VariableType), DataType, Unit, Min, Max, Address, IsStored, StoreMode, UpdateMode, PollingIntervalMs, BitOffset, DeadBand, ScaleSlope, ScaleOffset, IsReadOnly, ExtensionData` | `Type` 与 `DataType` 可矛盾；`IsStored`+`StoreMode` 重复 |
| `VariableTrigger` | `Name, DeviceId, VariableKey, Condition, Threshold, ActionType, AlarmLevel, LinkageVariableKey, LinkageValue, Active` | 报警/联动混装；三字段为 string |
| `MqttVariableConfig` | `MqttServerId, DeviceId, VariableKey, Alias, IsEnabled, CustomTopic` | 导出目标写死 |
| `DeviceRuntime` | `DeviceId, DeviceName, CurrentStatus, LastHeartbeat, ReconnectCount, LastError, LastCommunicationTime, IsConnecting, UptimeSeconds, SuccessCount, FailureCount` | 内存态 |

---

## 3. 重构前后详细对比

### 3.1 协议单一真相源（P0-1）

**Before**
```csharp
public class Device : EntityBase
{
    public DeviceType Type { get; set; }
    public string? DriverName { get; set; }   // 由 GetDefaultDriverName(Type) 派生但可覆盖
}

// 工厂两条路径并存
IProtocolDriver CreateDriver(DeviceType deviceType);
IProtocolDriver CreateDriver(string driverName);
```

**After**
```csharp
public class Device : EntityBase
{
    public DeviceType Type { get; set; }      // 唯一协议真相源
    // DriverName 已删除
}

// 工厂仅保留按 DeviceType 创建
public IProtocolDriver CreateDriver(DeviceType deviceType)
    => _registry.Resolve(deviceType);   // 见 3.9 注册表

// DataModel.Type 移除；若需区分型号，改为：
public string? ModelCategory { get; set; }  // 仅作"厂商/型号"描述，不参与驱动选择
```

### 3.2 信号类型由 DataType 推导（P0-2）

**Before**
```csharp
public VariableType Type { get; set; }      // Analog / Digital
public DataTypeEnum DataType { get; set; }
// 可出现 DataType=BOOL 但 Type=Analog 的矛盾
```

**After**
```csharp
// 删除 VariableType 列；提供派生属性
public DataTypeEnum DataType { get; set; }

[SqlSugar.SugarColumn(IsIgnore = true)]
public bool IsAnalog
    => DataType is not (DataTypeEnum.BOOL or DataTypeEnum.BIT);
```

### 3.3 存储策略枚举化（P0-3）

**Before**
```csharp
public bool IsStored { get; set; }
public string StoreMode { get; set; }   // 自由字符串
```

**After**
```csharp
public enum StoreMode { None = 0, Raw, Compressed, Aggregated }

public StoreMode StoreMode { get; set; } = StoreMode.Raw;
// None 等价于"不存储"；删除 IsStored
```

### 3.4 触发器拆分与枚举化（P0-4）

**Before**
```csharp
public class VariableTrigger : EntityBase
{
    public string Condition { get; set; }       // ">", "<", "=" ...
    public double Threshold { get; set; }
    public string ActionType { get; set; }      // "报警"/"联动"
    public string AlarmLevel { get; set; }      // "低"/"中"/"高"/"紧急"
    public string LinkageVariableKey { get; set; }
    public string LinkageValue { get; set; }
}
```

**After**
```csharp
public enum TriggerCondition { GreaterThan, LessThan, Equal, NotEqual, GreaterOrEqual, LessOrEqual }
public enum AlarmLevel { Low, Medium, High, Critical }

public class AlarmRule : EntityBase
{
    public int DeviceId { get; set; }
    public string VariableKey { get; set; }
    public TriggerCondition Condition { get; set; }
    public double Threshold { get; set; }      // 与变量同单位（工程值）
    public AlarmLevel Level { get; set; }
    public bool Active { get; set; }
}

public class LinkageRule : EntityBase
{
    public int DeviceId { get; set; }
    public string SourceVariableKey { get; set; }
    public TriggerCondition Condition { get; set; }
    public double Threshold { get; set; }
    public string TargetVariableKey { get; set; }
    public string TargetValue { get; set; }     // 写入前按目标变量 DataType 做类型校验
    public bool Active { get; set; }
}
```

### 3.5 Device.Key 唯一索引（P1-5）

**After**
```csharp
[SugarColumn(Length = 100, IsNullable = false, IsUnique = true)]
public string Key { get; set; } = string.Empty;
```
并在 `DeviceAppService.CreateAsync` 移除全表 `GetListAsync` 校验，交由数据库唯一约束 + 捕获唯一冲突异常（更优的并发安全性）。运行时查找走 `Dictionary<string, Device>`。

### 3.6 轮询间隔统一（P1-6）

**After**：两处均命名 `PollingIntervalMs`，注释明确"点位级覆盖设备级"：
```csharp
// Device.cs
public int PollingIntervalMs { get; set; } = 1000;
// ModelVariable.cs
public int PollingIntervalMs { get; set; } = 1000;  // 覆盖设备级默认值
```

### 3.7 配置版本与校验（P1-7）

**After**：`DeviceConfig` 更新时 `Version++`；新增 `ValidateConfigJson(type, json)` 在**创建与更新**两条路径均调用；实体加并发令牌（行版本）。

### 3.8 Min/Max 语义（P1-8）

**After**：在 `ModelVariable` 注释中明确 `Min/Max` 为**缩放后的工程值**，与 `Unit` 同单位；`AlarmRule.Threshold` 同样为工程值，二者可直接比较。

### 3.9 驱动注册表（P2-9）

**Before**：`ProtocolDriverFactory` 内 `switch` + `GetDefaultDriverName` + `DeviceTypeJsonConverter`，枚举 7 种仅实现 3 种。

**After**
```csharp
public interface IDriverRegistry
{
    IProtocolDriver Resolve(DeviceType type);
    void Register(DeviceType type, Func<IProtocolDriver> factory);
    bool IsSupported(DeviceType type);
}

// 启动时 DI 注册所有已实现驱动；未实现类型 IsSupported=false
// DeviceAppService.CreateAsync 校验 IsSupported，未实现则直接拒绝（而非运行时抛异常）
```

### 3.10 导出抽象（P2-10）

**After**
```csharp
public class ExportConfig : EntityBase
{
    public int DeviceId { get; set; }
    public ExportType Type { get; set; }     // Mqtt, Kafka, InfluxDb, Http ...
    public string TargetKey { get; set; }    // topic / measurement / url
    public string ConfigJson { get; set; }
}
// MqttVariableConfig 逐步迁移至 ExportConfig；过渡期双写
```

### 3.11 软删除与审计（P2-11）

**After**：`EntityBase` 增加 `IsDeleted`（过滤查询统一加 `IsDeleted=false`）；关键实体（Device/DataModel/ModelVariable）增加 `CreatedBy`、`UpdatedBy`。

### 3.12 通信时间双写（P2-12）

**After**：`Device.LastCommunicationTime` 仅作为持久化的"最后已知"快照（由运行时定期回写）；`DeviceRuntime.LastCommunicationTime` 为运行权威。业务读状态一律走运行时。

---

## 4. 目标领域模型（关键片段）

```csharp
public class Device : EntityBase
{
    public string Name { get; set; }
    [SugarColumn(Length = 100, IsNullable = false, IsUnique = true)]
    public string Key { get; set; }
    public int AreaId { get; set; }
    public int ModelId { get; set; }
    public DeviceType Type { get; set; }            // 唯一协议真相源
    public bool IsEnabled { get; set; } = true;
    public int PollingIntervalMs { get; set; } = 1000;
    public DateTime? LastCommunicationTime { get; set; }  // 最后已知快照
    public bool IsDeleted { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public class ModelVariable : EntityBase
{
    public int ModelId { get; set; }
    public string Key { get; set; }
    public string Name { get; set; }
    public DataTypeEnum DataType { get; set; }      // 信号类型自此推导
    public string? Unit { get; set; }
    public double? Min { get; set; }                // 工程值
    public double? Max { get; set; }
    public string Address { get; set; }
    public StoreMode StoreMode { get; set; } = StoreMode.Raw;  // 替代 IsStored+StoreMode
    public UpdateMode UpdateMode { get; set; }
    public int PollingIntervalMs { get; set; } = 1000;
    public int? BitOffset { get; set; }
    public double ScaleSlope { get; set; } = 1.0;
    public double ScaleOffset { get; set; } = 0.0;
    public double? DeadBand { get; set; }
    public bool IsReadOnly { get; set; } = true;
    public Dictionary<string, string>? ExtensionData { get; set; }

    [SugarColumn(IsIgnore = true)]
    public bool IsAnalog => DataType is not (DataTypeEnum.BOOL or DataTypeEnum.BIT);
}
```

---

## 5. 数据库迁移方案

采用"**加新字段 → 双写/回填 → 验证 → 下线旧结构**"的灰度策略，避免一次性大表改造风险。

### 5.1 阶段 A：新增与并存（兼容旧代码）
- 新增 `StoreMode`(`int`) 列；旧 `IsStored`/`StoreMode(string)` 保留。
- 新增 `AlarmRules`、`LinkageRules` 表；旧 `VariableTriggers` 保留。
- `Device.Key` 加唯一索引（低峰期执行，先 `CREATE UNIQUE INDEX ... WHERE ...` 或在线加）。
- `EntityBase` 加 `IsDeleted`；`Device/DataModel/ModelVariable` 加审计列。
- 代码层：写新表的同时保留旧表写入（双写），读路径暂仍用旧表。

### 5.2 阶段 B：回填与切换
- 数据回填脚本（示意）：
```sql
-- StoreMode 回填
UPDATE ModelVariables
SET StoreMode = CASE WHEN IsStored = 0 THEN 0
                    WHEN StoreMode = 'compressed' THEN 2
                    WHEN StoreMode = 'aggregated' THEN 3
                    ELSE 1 END;   -- 0=None,1=Raw,2=Compressed,3=Aggregated

-- 触发器拆分
INSERT INTO AlarmRules (DeviceId, VariableKey, Condition, Threshold, Level, Active)
SELECT DeviceId, VariableKey, Condition, Threshold,
       CASE AlarmLevel WHEN '低' THEN 0 WHEN '中' THEN 1 WHEN '高' THEN 2 ELSE 3 END, Active
FROM VariableTriggers WHERE ActionType = '报警';

INSERT INTO LinkageRules (DeviceId, SourceVariableKey, Condition, Threshold, TargetVariableKey, TargetValue, Active)
SELECT DeviceId, VariableKey, Condition, Threshold, LinkageVariableKey, LinkageValue, Active
FROM VariableTriggers WHERE ActionType = '联动';
```
- 读路径切换到新表/新字段；双写保留以便快速回滚。

### 5.3 阶段 C：下线旧结构
- 确认新路径稳定后，删除 `DriverName`、`VariableType` 列、`IsStored`、`StoreMode(string)` 列、`VariableTriggers` 表。
- `DataModel.Type` 移除或改为描述性 `ModelCategory`。

> 说明：SqlSugar 下可用 `db.CodeFirst.InitTables(...)` 做加列，删除类用原生 SQL 迁移脚本，并在迁移脚本中纳入版本管理（如 `Migrations/` 目录 + 执行记录表）。

---

## 6. 分阶段实施计划

| 里程碑 | 内容 | 产出 | 风险 |
|--------|------|------|------|
| M1 类型安全 | P0-2、P0-3、P0-4（枚举/拆分）+ 对应迁移 | 触发器分表、存储枚举 | 中（数据拆分需校验） |
| M2 真相源收敛 | P0-1 删除 `DriverName`/`DataModel.Type`，工厂单一路径 + 注册表（P2-9） | 驱动注册表、创建设备前置校验 | 低 |
| M3 健壮性 | P1-5 唯一索引、P1-6 命名、P1-7 版本校验、P1-8 语义 | 约束与校验 | 低 |
| M4 扩展性/运维 | P2-10 导出抽象、P2-11 软删除审计、P2-12 时间字段 | 新表/列 | 低 |

建议顺序：**M1 → M2 → M3 → M4**（先消一致性隐患，再做扩展）。

---

## 7. 风险与回滚

- **数据拆分风险**：`VariableTriggers` 拆分时若存在 `ActionType` 既非"报警"也非"联动"的脏数据，需先清洗。→ 迁移前跑一次 `SELECT` 审计。
- **唯一索引加锁**：大表加唯一索引可能锁表。→ 低峰期执行，或用在线 DDL。
- **回滚策略**：每个里程碑保留双写窗口；阶段 C 下线前保留旧列/表 1~2 个版本周期，发现异常即通过配置开关切回旧读路径。

---

## 8. 验收标准

- [ ] 单测：给定 `DataType=BOOL` 时 `IsAnalog==false`；`BIT/BOOL` 之外 `IsAnalog==true`。
- [ ] 单测：不存在 `Device.Type` 与 `DriverName` 不一致的可能（字段已删）。
- [ ] 单测：`Device.Key` 重复插入触发唯一约束异常。
- [ ] 单测：未实现协议在 `CreateAsync` 被拒（不抛运行时异常）。
- [ ] 集成：报警/联动分表后，原有触发行为等价。
- [ ] 迁移：阶段 B 回填脚本在预发环境跑通，行数一致。
- [ ] 回归：现有采集、存储、遥测接口行为不变。

---

## 9. 已拍板决策（2026-08-23）

| # | 问题 | 决策 |
|---|------|------|
| 1 | `DataModel.Type` | **改为厂商/型号描述字段保留**（不删除，语义由"协议类型"改为"厂商/型号"，如 "Siemens S7-1500"），与 `Device.Type`（协议真相源）解耦 |
| 2 | `VariableTriggers` | **必须分表**：拆为 `AlarmRules`（报警）与 `LinkageRules`（联动）两张表，概念彻底分离 |
| 3 | 软删除/审计 | **本期不做**，延后（不影响本次迁移） |
| 4 | 导出抽象 `ExportConfig` | **是**，优先级低于协议/触发器重构，放到后续里程碑 |

**设备编号（Key）方案：选 A**——前台可不填，后台按"区域编码 + 序号"自动生成（如 `BLR-001`），保留手动覆盖。
具体改动：新增 `Area.Code`（稳定短码，留空回退 `A{Id}`）；`CreateDeviceDto.Key` 改为可选；`Device.Key` 加唯一索引；`CreateAsync` 内 `GenerateDeviceCodeAsync` + `EnsureUniqueGeneratedKeyAsync` 保障生成与唯一性；前端放开 Key 必填校验并修复 `dev.code → dev.key` 的显示 bug。

> 注：`Area.Code` 的维护方式本期采用"手动填写（如 BLR），留空则回退 A{Id}"，未引入拼音自动转换依赖。

**实施状态（2026-08-23，已完成）**：
- `Server/ScadaServer.Domain/Entities/Area.cs`：新增 `Code` 字段（`string?`），长度 50。
- `Server/ScadaServer.Domain/Entities/Device.cs`：`[SugarIndex("ix_device_key", nameof(Key), OrderByType.Asc, true)]` 唯一索引（放在类上）。
- `Server/ScadaServer.Application/DTOs/CreateDeviceDto.cs`：`Key` 去掉 `[Required]`，改为 `string?`。
- `Server/ScadaServer.Application/Services/DeviceAppService.cs`：新增 `GenerateDeviceCodeAsync` / `EnsureUniqueGeneratedKeyAsync`；`CreateAsync` 在未提供 Key 时自动生成并确保唯一。
- `Server/ScadaServer.Infrastructure/Persistence/DatabaseInitializer.cs`：新增 `EnsureDeviceKeyUniqueIndex()`，对存量库幂等补齐唯一索引（CodeFirst.InitTables 不会给已存在表加索引）。
- `Client/.../DeviceManagementView.vue`：放开 Key 必填校验（仅校验 Name）；提交时 `key` 可为空串。
- `Client/.../TriggerManagementView.vue`：修复 `dev.code → dev.key` 显示 bug（`DeviceDto` 返回的是 `key`）。
- 验证：Domain / Application / Infrastructure 均编译通过（0 错误）。
