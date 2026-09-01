# 时间处理 UTC 标准化改造方案（仅方案，未改动任何代码）

> 目标：把项目中所有“产生时间”的代码统一为 `DateTime.UtcNow`，不再使用 `DateTime.Now` / `DateTime.Today`。
> 范围：后端 `Server/`（371 个 `.cs`）。前端 `Client/`（Vue + `new Date()`）列为决策点，未纳入本轮。
> 结论先行：**后端已经高度 UTC 化**，真正需要改的只有 3 处 `DateTime.Now`，更大的隐患在“外部时间戳未归一化”和“读回 `Kind=Unspecified` 未统一修复”。

---

## 0. 结论速览

| 项 | 现状 |
|---|---|
| `DateTime.Now` / `DateTime.Today` 字面量 | **仅 3 处**，全部在清理类 `HostedService` 的“每日 3 点执行”调度判断里 |
| `DateTime.UtcNow` | ~60+ 处，实体默认值、运行时采集、日志、清理 cutoff 等已全面使用 |
| `DateTimeOffset` | 0 处（无时区感知类型，全靠“约定 UTC”） |
| `DateTime.MinValue` / `MaxValue` 哨兵 | 5 处（4×MinValue + 1×MaxValue），需逐一评估 |
| 数据库列类型 | MySQL `datetime(6)`（**非** `timestamp`），原样存储、无时区转换 |
| 集中时钟抽象（`TimeProvider`/`IClock`） | 无 |
| 测试项目 `DateTime.Now` | 0 处 |

**真正的风险不是 `DateTime.Now` 多，而是：**
1. 3 处 `DateTime.Now` 承载“3AM 本地”语义，盲目替换会静默改变执行时间（见 P0-1）。
2. 外部传入的时间戳（日志 DTO、传感器 DTO、历史查询区间）未归一化为 UTC，客户端若发本地时间会被当 UTC 存（差 8h，见 P0-2）。
3. 从 `datetime(6)` 读回的 `DateTime` 是 `Kind=Unspecified`，目前仅 `AlarmRecordAppService` 做了 `SpecifyKind(Utc)`，其他 DTO 映射未做，JSON 不带 `Z`，前端可能按本地时间解析（见 P1-1）。
4. Influx 读回 `null` 时间时回退 `DateTime.MinValue`，产生 0001 年脏数据（见 P1-2）。

---

## 1. 现状统计（按层 / 文件）

| 文件 | 行 | 用法 | 是否 UTC |
|---|---|---|---|
| `HostedServices/SystemLogCleanupHostedService.cs` | 68 | `DateTime.Now`（仅用于 `Hour==3` 调度判断） | ❌ 本地 |
| `HostedServices/ScriptExecutionRecordCleanupHostedService.cs` | 59 | `DateTime.Now`（同上） | ❌ 本地 |
| `HostedServices/AlarmRecordCleanupHostedService.cs` | 64 | `DateTime.Now`（同上） | ❌ 本地 |
| `Runtime/Tasks/ScheduledTaskScheduler.cs` | 115/201/207/267/268/285 | `DateTime.UtcNow` | ✅ |
| `Runtime/Devices/DeviceWorker.cs` | 107/270/564/574/755 | `DateTime.UtcNow` / `MaxValue`(124) | ✅ |
| `Runtime/Devices/DeviceScheduler.cs` | 134/140/152/271 | `DateTime.UtcNow` | ✅ |
| `Runtime/Bindings/VariableBindingEngine.cs` | 253 | `DateTime.UtcNow` | ✅ |
| `Runtime/Scripting/ScriptEngineHost.cs` | 115(MinValue)/243/340/429/506/557/860/945 | `UtcNow` + `MinValue` 哨兵 | ✅ |
| `Runtime/Variables/VariableRuntime.cs` | 80/118 | `MinValue` 哨兵（运行时对象） | ⚠️ Unspecified |
| `Runtime/RuntimeManager.cs` | 435/611 | `DateTime.UtcNow` | ✅ |
| `WebApi/Controllers/TelemetryDataController.cs` | 46 | `UtcNow` | ✅ |
| `WebApi/Controllers/ModelVariableController.cs` | 109 | `UtcNow:yyyyMMddHHmmss`（文件名） | ✅ |
| `WebApi/Controllers/HistoryController.cs` | 103 | `UtcNow`（导出文件名） | ✅ |
| `WebApi/Middlewares/ExposedApiMiddleware.cs` | 91/98 | `UtcNow` + `NormalizeUtc`→`Z` | ✅ |
| `WebApi/HostedServices/SystemLogRecorder.cs` | 81/109 | `UtcNow` | ✅ |
| `WebApi/HostedServices/*Cleanup*.cs` | 112/90/98 | `cutoff = DateTime.UtcNow.AddDays(-n)` | ✅（cutoff 已 UTC） |
| `WebApi/HostedServices/AlarmRecordCleanupHostedService.cs` | 64 | `DateTime.Now`（调度） | ❌ 本地 |
| `Domain/Entities/*.cs`（Protocol/DataModel/Device/DeviceConfig/DbVersion） | — | 属性默认 `= DateTime.UtcNow` | ✅ |
| `Application/Services/*.cs`（多文件） | 多处 | `UtcNow` 写库 | ✅ |
| `Application/Services/SystemLogAppService.cs` | 63/82 | `dto.Timestamp` 透传（外部输入，**未归一化**） | ⚠️ |
| `Application/Services/SensorAppService.cs` | 61/77 | `dto.LastUpdateTime` 透传 | ⚠️ |
| `Application/Services/HistoryAppService.cs` | 37/85/128/165 | 查询区间 `start/end` 透传比较 | ⚠️ |
| `Infrastructure/Services/SystemMonitorService.cs` | 115 | `UtcNow - process.StartTime.ToUniversalTime()` | ✅ |
| `Infrastructure/Influx/InfluxStore.cs` | 302/491/505/741/779/784 | `MinValue` 哨兵 / `ToUniversalTime` / epoch 常量 | ⚠️(302) |
| `Infrastructure/Communication/OpcUaDriver.cs` | 807/938 | `UtcNow`（`_nextReconnectAttemptUtcTicks`） | ✅ |
| `Infrastructure/Communication/MqttManager.cs` | 297/393 | `UtcNow` | ✅ |
| `Infrastructure/Persistence/DatabaseInitializer.cs` | 127/128/182 | `UtcNow` | ✅ |
| `Infrastructure/Migrations/20260827105828_AddSystemUserCreatedAt.cs` | 20/22 | 注释明确“回填 UTC，避免本地/UTC 混用差 8h” | ✅（历史已迁移） |

> 结论：一次历史迁移（`AddSystemUserCreatedAt`）已把存量 `CreatedAt` 统一为 UTC，代码侧绝大部分已是 `UtcNow`。

---

## 2. 问题清单（按严重度，含 Before/After 与决策点）

### P0 — 运行时正确 / 语义风险

#### P0-1　三个清理服务的 `DateTime.Now` 调度语义
`Before`（现状）：
```csharp
// SystemLogCleanupHostedService.cs:68（另两个文件同构）
var now = DateTime.Now;
// 每天 3 点执行一次（本地时间）
if (now.Hour == 3 && _lastCleanupDate != now.Date)
{
    _lastCleanupDate = now.Date;
    await CleanupAsync(stoppingToken);   // cutoff 内部另用 DateTime.UtcNow
}
```
`After`（方案 A，直接 UTC，行为变为“3AM UTC=11AM CST”）：
```csharp
var now = DateTime.UtcNow;
if (now.Hour == 3 && _lastCleanupDate != now.Date) { ... }
```
`After`（方案 B，保留 CST 3AM 语义，推荐——用配置化 UTC 小时或显式转换）：
```csharp
// 配置 MaintenanceHourUtc（CST 3AM → 19），或：
var now = DateTime.UtcNow;
if (now.Hour == _options.MaintenanceHourUtc && _lastCleanupDate != now.Date) { ... }
```
> ⚠️ **决策点 D1**：盲目 `DateTime.Now→UtcNow` 会把执行时间从“服务器本地 3AM”静默改为“UTC 3AM = 北京 11AM”，影响运维窗口。**必须按 D1 决策处理，不能直接全量替换。**

#### P0-2　外部 DTO / 查询时间戳未归一化为 UTC
`SystemLogAppService.cs:63`：
```csharp
Timestamp = dto.Timestamp == default ? DateTime.UtcNow : dto.Timestamp; // 外部值原样用
```
`SystemLogAppService.cs:82` / `SensorAppService.cs:61,77` / `HistoryAppService` 查询 `start/end`：均为 `dto.X` 透传。
风险：若前端/客户端以本地时间发送（不带 `Z`），会被当成 UTC 存储或参与比较，产生 8h 偏差。
`After`（边界归一化，推荐在 AppService 入口统一）：
```csharp
Timestamp = dto.Timestamp == default ? DateTime.UtcNow
          : DateTime.SpecifyKind(dto.Timestamp, DateTimeKind.Utc);
```
> ⚠️ **决策点 D2**：A) 约定客户端一律发 UTC（`toISOString()`，带 `Z`），后端信任；B) 后端在 API 边界 `SpecifyKind(...,Utc)` 兜底（推荐，更稳，即使客户端发本地也能修正为 UTC 解释）。

### P1 — 一致性 / 健壮性

#### P1-1　读回 `Kind=Unspecified` 未统一修复
`AlarmRecordAppService.cs:77,93-100` 已正确：
```csharp
// MySQL datetime 读回 Kind=Unspecified，需显式标记 UTC 保证 JSON 带 Z
TriggeredAt = DateTime.SpecifyKind(e.TriggeredAt, DateTimeKind.Utc);
```
但 `SystemLog` / `Sensor` / `History` / `RealtimeSnapshot` 等 DTO 映射未做同样处理 → 序列化无 `Z`，前端可能按本地时间解析（+8h）。
`After`：抽取统一辅助方法（已有模范 `ExposedApiMiddleware.NormalizeUtc`），在所有 DTO 映射处调用：
```csharp
static DateTime ToUtc(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Utc);
```

#### P1-2　Influx 读回 `null` 的 `MinValue` 哨兵
`InfluxStore.cs:302`：
```csharp
var timestamp = time ?? DateTime.MinValue;   // Influx 无时间 → 0001 年脏数据
```
`After`：跳过该记录，或回退 `DateTime.UtcNow`（视业务语义），不要写 `0001-01-01`。

#### P1-3　数据库列类型 `datetime(6)` vs `timestamp`
现状：所有时间列为 `datetime(6)`，MySQL 原样存储、无时区转换。当前正确性完全依赖“全程 UTC 纪律”。
> ⚠️ **决策点 D4**：是否迁移为 `timestamp`（MySQL 自动按 UTC 存储、按会话时区读回）？优点：存储层兜底时区；代价：需迁移脚本 + 确认存量数据全部视为 UTC（无 local 写入）。

### P2 — 工程化 / 长期

#### P2-1　缺少集中时钟抽象
无 `TimeProvider`/`IClock`。.NET 8 自带 `TimeProvider`，可注入以便单元测试（如验证清理服务在指定小时触发）。
> ⚠️ **决策点 D3**：是否引入 `TimeProvider` 抽象（更大改造，但提升可测性）。

#### P2-2　无“禁止 `DateTime.Now`”的静态强制
当前仅靠约定。建议加 `.editorconfig` 规则或 CI 脚本（grep `DateTime.Now`）防止回归。

#### P2-3　测试项目
`ScadaServer.Infrastructure.Tests` 已确认无 `DateTime.Now`，无需改动。

### 备注（非 `DateTime.Now`，但相关，确认无需改）
- `InfluxStore.cs:505` `new DateTime(1970,1,1,...)` = Unix epoch 常量（Influx 删除 API 用），正确。
- `SystemMonitorService.cs:115` `process.StartTime.ToUniversalTime()` 已正确转 UTC。
- `ExposedApiMiddleware.cs:98` `dt?.ToUniversalTime().ToString("...'Z'")` 输出带 `Z`，正确。
- `VariableRuntime` / `DeviceWorker` 的 `MinValue`/`MaxValue` 哨兵为运行时对象（非落库），仅做最小/最大比较，Kind 不影响结果，可暂不动；若日后落库需关注。

---

## 3. 推荐修改方案（总原则）

**后端一律以 UTC 产生、存储、传输；本地化展示由前端按用户时区完成。**

1. **P0-1**：按 D1 决策替换 3 处 `DateTime.Now`；推荐方案 B（配置化 `MaintenanceHourUtc`，保留 CST 3AM 运维窗口）。
2. **P0-2 + P1-1**：新增统一辅助 `ToUtc(DateTime)` / 复用 `NormalizeUtc`，在（a）所有 DTO→实体入口（SystemLog/Sensor/历史查询区间）和（b）所有实体→DTO 映射处调用，保证“入参标 UTC、出参带 Z”。
3. **P1-2**：修复 Influx 空时间哨兵（跳过或回退 `UtcNow`）。
4. **（可选）P1-3 / P2-1 / P2-2**：列类型迁移、`TimeProvider` 抽象、CI 禁 `DateTime.Now` 规则，按 D3/D4 决策后单独成项。

---

## 4. 决策点汇总（待你确认后再动手）

- **D1**：三个清理服务的执行时间 —— A) 改 3AM UTC（直接 `UtcNow.Hour==3`，行为变 11AM 北京）；B) 保留 CST 3AM（映射 `19:00 UTC` 或 `TimeZoneInfo.Local` 计算，推荐）；C) 引入 cron/Quartz 显式指定时区。
- **D2**：外部时间来源 —— A) 约定客户端一律发 UTC（带 `Z`）；B) 后端边界 `SpecifyKind(...,Utc)` 兜底（推荐）。
- **D3**：是否引入 `TimeProvider` 抽象（可测性 vs 改造量）。
- **D4**：`datetime(6)` 是否迁移 `timestamp`。
- **D5**：前端 `Client/`（Vue，`new Date()`）是否也纳入本轮 UTC 统一（需另出前端方案）。

---

## 5. 影响与风险

- P0-1 若不走 D1 直接全量替换，清理窗口静默偏移 8h，**务必先决策**。
- P1-3 改列类型需迁移脚本，且须确认存量数据无 local 写入（历史迁移已声明全 UTC，风险低）。
- “纯计时/比较”场景 `Now→UtcNow` 安全；但凡涉及“本地墙钟展示/调度”必须先走 D1/D2，否则前端时间整体偏移 8h。

---

## 6. 验证（你要求“只改代码、不要构建”，以下命令供你执行）

```bash
# 编译（不自动触发）
dotnet build Server/ScadaServer.WebApi/ScadaServer.WebApi.csproj -c Debug

# 静态确认无残留 DateTime.Now
grep -rn "DateTime\.\(Now\|Today\)" Server --include=*.cs

# 单元：注入假时钟验证清理服务在目标小时触发（需 P2-1 后更易测）
# 集成：用本地时间 DTO 写入，断言落库为 UTC（差 8h 修正）
# 历史查询：以本地时间区间查询，断言按 UTC 解释
```

---

## 7. 范围说明

本轮仅分析后端 `Server/`，未改动任何代码。前端 `Client/`（Vue）存在 `new Date()` 等时间用法，若要“所有时间”一并统一，请确认 D5，我单独出前端 UTC 方案。
