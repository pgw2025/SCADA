# SCADA 后端项目分析文档

> 定位：单人维护的 SCADA（数据采集与监控）后端工程逆向梳理文档。本文档只描述**代码里真实存在的内容**，凡未在代码/配置中确认的信息一律标记【缺少文件，无法确认】，不做任何脑补。
>
> 分析基准：`Server/` 目录代码 + 配置文件（2026-09）。文档会随你后续补充新文件而版本连贯更新（见第 10 节）。

***

## 1. 项目概述

- **功能**：一套面向工业现场的 SCADA 服务器后端。核心能力是「**采集物理设备数据 → 在内存中维护实时变量 → 推送前端并持久化历史 → 反向下发控制 / 联动 / 报警 / 脚本 / 定时任务**」。

- **业务目标**：让现场设备（Siemens S7 PLC、OPC UA 服务器等）接入一套统一的后端，前端（HMI 组态/看板）通过 SignalR 实时订阅变量，运维可通过 HTTP API、定时任务、JS 脚本、或开放 API 网关读写设备变量。

- **使用场景**：工业监控大屏、设备远程运维、历史趋势查询、报警管理、组态画面运行、面向第三方系统的开放数据接口。

**领域模型一句话**：设备 `Device` → 绑定数据模型 `DataModel` → 数据模型绑定协议 `Protocol` → 数据模型下挂模型变量 `ModelVariable`；设备实例再通过 `DeviceVariable`（实例级地址/轮询/启停）落到运行时的 `VariableRuntime`。变量全局唯一身份由「`DeviceKey + VariableKey`」共同确定。

***

## 2. 技术栈清单

### 工程与运行时

| 项     | 内容                                                                       |
| ----- | ------------------------------------------------------------------------ |
| 语言/框架 | C#，.NET 8（`net8.0`），ASP.NET Core Web API，EF Core 8                       |
| 架构风格  | Clean / DDD 分层（Domain → Application → Infrastructure → Runtime → WebApi） |

### 数据库

| 项         | 内容                                                                       |
| --------- | ------------------------------------------------------------------------ |
| 主库（业务/配置） | MySQL（`Pomelo.EntityFrameworkCore.MySql 8.0.3` + `MySqlConnector 2.6.2`） |
| 历史时序库     | InfluxDB 2.x（`InfluxDB.Client 5.1.0`）                                    |

### 通信协议 / 第三方 NuGet

| 用途              | 包                                                                                               | 说明                                     |
| --------------- | ----------------------------------------------------------------------------------------------- | -------------------------------------- |
| Siemens S7 PLC  | `S7netplus 0.20.0`                                                                              | 点读/批量读/位写（已完整实现）                       |
| OPC UA          | `OPCFoundation.NetStandard.Opc.Ua 1.5.378.145`                                                  | 会话/KeepAlive/批量读（已完整实现）                |
| MQTT            | `MQTTnet 5.1.0.1559`                                                                            | 对外发布/类 MQTT 服务器管理                      |
| JS 脚本沙箱         | `Jint 3.1.3`                                                                                    | 受限 JS 引擎（Runtime/Application 各引一次版本一致） |
| Cron 表达式        | `Cronos 0.13.0`                                                                                 | 定时任务调度                                 |
| Excel 导入导出（TIA） | `ClosedXML 0.104.2`                                                                             | 模型变量 xlsx 导入导出                         |
| JWT             | `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.0` / `System.IdentityModel.Tokens.Jwt 7.1.2` | 认证授权                                   |
| 密码散列            | `Microsoft.AspNetCore.Identity 2.3.10`                                                          | 依赖引入（散列方案是否实际启用见第 5/8 节）               |
| 系统监控            | `System.Diagnostics.PerformanceCounter 10.0.8`                                                  | 机器/进程指标采集                              |
| API 文档          | `Swashbuckle.AspNetCore 6.5.0`                                                                  | Swagger UI（当前无条件启用）                    |
| 单测              | xUnit（`S7DriverAddressParsingTests`）                                                            | 目前仅 S7 地址解析                            |

### 外部依赖服务

- MySQL（主库）、InfluxDB（时序）、S7 PLC、OPC UA Server、外部 MQTT Broker、前端（Vite + Vue3，通过 `/api` 与 `/hubs/*` 反向代理到本后端）。

### 自建协议恒定约定

- **时间戳一律 UTC**，前端负责本地化；对外 `timestamp_utc`。

- 实时业务数据→MySQL；历史时序数据→InfluxDB（measurement `variable_history`，tag `device_key` + `variable_key`，其余为字段）。

***

## 3. 目录结构解析

后端根目录 `Server/` 下：

| 目录/文件                               | 用途                                        | 归类            |
| ----------------------------------- | ----------------------------------------- | ------------- |
| `ScadaServer.slnx`                  | 解决方案（新 XML 格式）                            | 工程            |
| `ScadaServer.Domain/`               | **领域层**：实体、枚举、领域接口、异常、常量                  | 核心业务          |
| `ScadaServer.Application/`          | **应用层**：DTO、服务契约与实现、导入导出、配置选项、Json 转换器    | 核心业务          |
| `ScadaServer.Infrastructure/`       | **基础设施层**：EF 持久化+迁移、仓储、通信驱动、Influx、系统监控   | 核心业务（驱动/存储重点） |
| `ScadaServer.Runtime/`              | **运行期引擎**：设备调度/采集/报警/绑定/脚本/定时任务           | 核心业务（实时引擎重点）  |
| `ScadaServer.WebApi/`               | **宿主层**：Program、控制器、DI 组合根、中间件、Hub、后台托管服务 | 核心业务（出入口）     |
| `ScadaServer.Infrastructure.Tests/` | 单元测试（仅 S7 地址解析）                           | 测试            |
| `.gitignore`                        | Git 忽略                                    | 配置            |
| `设计规范.md`                           | 项目级规范说明                                   | 文档            |

### 各层内部明细

- **Domain**：`Entities/`（实体，含 EF 特性）、`Enums/`（DeviceType/StoreModeEnum/Quality 等）、`Interfaces/`（`IProtocolDriver`、`IRuntimeDevice`/`IRuntimeVariable`、仓储接口）、`Exceptions/`（`BusinessException` 等）、`Constants/`（`SystemRoles`）。

- **Application**：`DTOs/`、`Interfaces/`（`I*AppService` 应用服务契约 + `IHistoryRecorder`/`IAlarmRecorder` 等运行时契约）、`Services/`（各 `AppService` 实现 + `ExposedApiRegistry`）、`ImportExport/`（CSV/TIA xlsx 解析 + 导出）、`Options/`（`SystemDbOptions`/`SystemLogOptions`/`HmiImageOptions`）、`Converters/`（枚举/object JSON 转换）。

- **Infrastructure**：`Persistence/`（`ScadaDbContext`、`DatabaseInitializer`、EF 迁移）、`Repositories/`、`Communication/`（`S7Driver`/`OpcUaDriver`/`VirtualDriver`/`ProtocolDriverFactory`/`DeviceRegistry`/`MqttManager`/`MqttHandler`）、`Influx/InfluxStore.cs`、`Services/`（`RuntimeDatabaseService`、`HistoryMigrationService`、`SystemMonitorService`）。

- **Runtime**：`RuntimeManager.cs`、`Devices/`（`DeviceScheduler`/`DeviceWorker`/`DeviceRuntime`）、`Variables/`、`Events/`、`Alarms/`、`Bindings/`、`Scripting/`、`Tasks/`。

- **WebApi**：`Program.cs`、`Controllers/`（30 个）、`Extensions/`（DI + 中间件管道）、`Middlewares/`、`Hubs/`、`HostedServices/`、`Services/`、`Filters/AuditLogAttribute.cs`、`Logging/DatabaseLoggerProvider.cs`。

> ⚠️ 关注点：解决方案 `ScadaServer.slnx` 只列了 `Application/Domain/Infrastructure/WebApi` 四个工程；`Runtime` 因被 `WebApi.csproj` 引用仍会参与构建，但 **`Infrastructure.Tests`** **不在解决方案内，用 slnx 直接构建不会跑/只会单独编译**。

***

## 4. 整体架构 & 分层说明

依赖方向**单向无环**（Runtime 文档 `docs/runtime-architecture.md` 为权威说明）：

```
WebApi（组合根/宿主：DI、Controllers、Hubs、中间件、IHostedService）
   │ 注入 Singleton 依赖
   ▼
Runtime（实时引擎：RuntimeManager→Scheduler→Worker，Alarms/Bindings/Scripting/Tasks）
   │ 消费 Application 契约（IRuntimeDeviceManager/IScadaNotificationService/IRecorder…）
   │ 依赖 Infrastructure（DeviceRegistry/ProtocolDriverFactory/IInfluxStore/ScadaDbContext）
   ▼
Domain（实体/枚举/领域接口，驱动只可见 IRuntimeDevice/IRuntimeVariable）
```

各层职责与「禁止跨层」约束：

| 层                  | 职责                                              | 边界约束                                                                                                                     |
| ------------------ | ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| **Domain**         | 实体、枚举、领域接口（含 `IProtocolDriver`）、仓储接口。**零外部依赖**。 | 不含任何业务流程与 EF/驱动实现                                                                                                        |
| **Application**    | `I*AppService` 契约 + DTO + 导入导出 + 配置选项 + 应用服务实现  | 不含 ASP.NET Core 具体类型（`HmiImageAppService` 经工厂注入 `IWebHostEnvironment` 规避）；`I*Recorder` 契约定义在此，实现靠 WebApi 的 HostedService |
| **Infrastructure** | EF 持久化、仓储、通信驱动、Influx、系统监控                      | 驱动**只感知** **`IRuntimeDevice`/`IRuntimeVariable`**，严禁感知 `DataModel`/`ModelVariable` 模板实体                                  |
| **Runtime**        | 设备生命周期、采集调度、报警、绑定、脚本、定时任务                       | 不负责持久化写入（走异步落库通道）；依赖单向，不回注 WebApi                                                                                        |
| **WebApi**         | 组合根、Controllers、Hubs、中间件、后台 Record 服务、日志写库      | 只做编排，不落核心算法                                                                                                              |

**关键解耦点**：`RuntimeManager`（Singleton）主动调用 `IScadaNotificationService`，但通知实现**不回注** `RuntimeManager`，避免 Singleton 循环依赖。

***

## 5. 核心业务数据流

### 数据流 A：实时采集（心跳主线）

```
DeviceScheduler（每 50ms tick）
   └─ DispatchWorker（锁内：校验在册→置 IsRunning→建 workerCts→记 Task）
        └─ DeviceWorker.WorkerAsync（每台设备一个常驻循环）
             ├─ 收集 NextPollTime<=now 的到期变量
             ├─ Driver.ReadAsync(vr)（S7 批量读可聚簇减少往返）
             │   ├─ 成功：Lock 内更新 Value/Previous/Quality/IsChanged
             │   └─ null/异常：Quality=CommunicationError（质量降级）
             ├─ VariableChangeBus.Publish(VariableChangeEvent)【进程内总线】
             ├─ SignalR NotifyVariableUpdateAsync（fire-and-forget 推送）
             ├─ IHistoryRecorder 异步入队 → 批量写 InfluxDB（失败回退 MySQL）
             ├─ IRealtimeSnapshotService 异步更新 → 每秒 Upsert 到 VariableRealtime
             └─ AlarmRuleEngine 评估报警 → NotifyAlarmAsync + AlarmRecorder 落库
       轮次级：anySuccess→Connected；连续 3 轮失败→NeedsReconnect，退出
   └─ 调度器发现 NeedsReconnect → 退避5s → RuntimeManager.ReconnectDeviceAsync（自愈）
```

### 数据流 B：用户下发（HTTP 写）

```
HTTP → DeviceController.WriteVariable（[Authorize]，审计过滤器记录操作人/IP）
   └─ RuntimeManager.WriteVariableAsync(deviceId, variableKey, value, writeSource)
        ├─ 校验：设备在运行/变量存在/未禁用/未只读/驱动就绪/已连接
        ├─ 服务端数值限幅（前端 min/max 可被绕过，后端强校验）
        ├─ Driver.WriteAsync(vr, value).WaitAsync(设备写超时兜底)
        ├─ 锁内同步内存值 + SignalR 广播 + 发布写事件 + 更新实时快照
        └─ 非 HTTP 来源（脚本/绑定）记录写入审计日志（writeSource 非空时）
```

### 数据流 C：报警

```
DeviceWorker.TryCheckAlarm
   └─ AlarmRuleEngine.GetRules(deviceId, variableKey) → 命中规则为权威（防抖+去重状态机）
      无规则 → 回退 ModelVariable.Min/Max 兜底（来源=MinMaxLimit）
   ├─ NotifyAlarmAsync（实时推送）
   └─ AlarmRecorder 异步落库 AlarmRecords（Ack/Recover 状态）
```

### 数据流 D：变量绑定联动

```
VariableChangeBus → VariableBindingEngine（订阅者，非阻塞入队有界 Channel）
   └─ 命中索引 → 转发写入目标变量（源设备.源变量 → 目标列表）
      加载期做自环/多跳环检测；转发后回声抑制（LastBindingWriteValue/Time）
```

### 数据流 E：JS 脚本

```
ScriptEngineHost（250ms Tick 驱动 Periodic/Cron；onChange 由事件总线驱动）
   └─ 入队到有界派发队列（队满丢弃+计数告警）
   └─ 消费者线程 → ScriptSandbox(Jint: Strict+LimitRecursion+超时)
       暴露白名单 API：read / getQuality / write / log（read/write 受作用域授权）
   └─ 结果：成功/失败 → 更新熔断（连续3次失败 Tripped）→ PersistRecordAsync 落库
        → TransmitAsync 经 SignalR 推送执行结果
```

### 数据流 F：定时任务 / 开放 API

```
ScheduledTaskScheduler（1s Tick，Cronos 解析，10min 兜底重载）
   └─ 按 Task.Type 策略分派 IScheduledTaskExecutor：
        SetValueTaskExecutor → WriteVariableAsync
        ExecuteScriptTaskExecutor → scriptEngine.RunAsync
        ClearHistoryTaskExecutor → influxStore.DeleteBeforeAsync(retentionDays)
        BackupTaskExecutor → 导出 MySQL 表 + InfluxDB 数据 + manifest → 打包 zip（Backups 目录）
   状态 Running/Success/Failed/Skipped 回写 ScheduledTasks 表

/open/* → ExposedApiMiddleware（终端处理器，匹配 IExposedApiRegistry 启用的接口）
   → 从 Runtime 实时读目标变量最新值 → 统一 JSON 契约返回
```

***

## 6. 核心入口文件、关键类说明

### 启动入口与配置加载

- **`Program.cs`**（WebApi）：构建 Host → 叠加可选 `appsettings.dboverride.json`（主库连接，改后需重启生效）→ 出程序集配置 `SystemDbOptions`/`SystemLogOptions`/`HmiImageOptions` → 注册 `DatabaseLoggerProvider` → `AddAuthenticationServices`/`AddDatabaseServices`/`AddApplicationServices`/`AddInfrastructureServices` → 注册各 `IHostedService` → 中途改用手动 `StartAsync`+`WaitForShutdownAsync`（避免 RunAsync 在启动失败后提前 Dispose Host 导致二次崩溃）→ 兜底全局异常（`AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException`，自身绝不抛异常）。含端口预检友好提示。

- **`Extensions/WebApplicationExtensions.cs`**：中间件管道顺序 **CORS → ExceptionMiddleware → Swagger →** **`/open`** **网关分支 → UseRouting → UseAuthentication/UseAuthorization → MapControllers → MapHub(/hubs/scada, /hubs/systemlog)**。注：`UseHttpsRedirection` 被注释。

- **`Extensions/Authentication.Extensions.cs`**：JWT Bearer；`OnMessageReceived` 从 `access_token` 查询参数为 `/hubs` 提取 token（WebSocket 无法带 Header）；**FallbackPolicy=RequireAuthenticatedUser**（默认所有端点必须登录），另加 `RequireAdmin` 策略；登录接口 \[AllowAnonymous]。

- **`Extensions/*.Extensions.cs`**：三个 DI 组合根，见附表（第 4 节）；单例/Scoped/HostedService 生命周期边界严格。

### 关键业务类（分层）

| 类                                                                               | 所在层                   | 作用                                                                                  |
| ------------------------------------------------------------------------------- | --------------------- | ----------------------------------------------------------------------------------- |
| `RuntimeManager`                                                                | Runtime               | 运行时总控：设备注册/重连/注销，`WriteVariableAsync`（校验+限幅+超时+审计），`InitializeAsync` 加载启用设备图，启停绑定引擎 |
| `DeviceScheduler`                                                               | Runtime/Devices       | 每 50ms tick 派发每设备唯一 Worker；退避表 + `RetryBackoff=5s`；Worker 意外退出自愈重拉                  |
| `DeviceWorker`                                                                  | Runtime/Devices       | 单设备采集循环：按 `NextPollTime` 轮询，更新内存值、发事件、推数据、报警评估；连续失败置 NeedsReconnect                 |
| `VariableRuntime`                                                               | Runtime/Variables     | 变量运行时（实例+模板解析结果）：地址、间隔、缩放、死区、质量、值                                                   |
| `VariableChangeBus`                                                             | Runtime/Events        | 进程内事件总线；**同步快照调用 + 逐订阅者 try/catch**，单订阅者失败不影响采集                                     |
| `AlarmRuleEngine`                                                               | Runtime/Alarms        | 30s 定时拉活跃规则 → 不可变快照整体替换；CRUD 后 `ReloadAsync` 即时生效                                   |
| `VariableBindingEngine`                                                         | Runtime/Bindings      | 变量转发联动：环检测 + 声抑制 + 有界 Channel 背压                                                    |
| `ScriptEngineHost`/`ScriptSandbox`/`ScriptRuntimeAccess`                        | Runtime/Scripting     | 脚本调度/沙箱/读写授权桥                                                                       |
| `ScheduledTaskScheduler` + 4 个 Executor                                         | Runtime/Tasks         | 定时任务单 Tick 驱动 + 策略执行器                                                               |
| `S7Driver`/`OpcUaDriver`/`VirtualDriver`                                        | Infrastructure        | 协议驱动（见第 7 节）                                                                        |
| `ExposedApiRegistry`/`IExposedInterfaceAppService`                              | Application           | `/open` 网关的缓存注册表与配置                                                                 |
| `ScadaDbContext`                                                                | Infrastructure        | EF 上下文：表名、唯一索引、外键 Restrict、longtext 列显式声明                                           |
| `DatabaseInitializer`                                                           | Infrastructure        | 迁移 + 种子数据（由 StartupHostedService 驱动）                                                |
| `HistoryRecorder`/`AlarmRecorder`/`SystemLogRecorder`/`RealtimeSnapshotService` | WebApi/HostedServices | 异步批量落库后台服务（有界 Channel，DropWrite 背压）                                                 |

### 公共工具 / 模式

- **异步批量落库模式**：`Channel.CreateBounded` + `DropWrite`（历史/报警/运行日志有界可丢；操作/安全日志无界不丢）。采集线程绝不阻塞。

- **配置热重载**：`InfluxStore.Rebuild` 通过 `Interlocked.Exchange` 原子替换客户端，变更即时生效；主库配置则需重启。

- **DTO/转换器**：`DataTypeEnumJsonConverter`/`DeviceTypeJsonConverter` 统一前后端枚举命名；`ObjectClrTypeJsonConverter` 让 `object` 反序列化为 CLR 类型。

- **过滤**：`AuditLogAttribute` 操作审计；`ExceptionMiddleware` 把 `BusinessException` 映射为对应状态码、其余 500（开发环境带堆栈）。

***

## 7. 外部依赖 & 第三方交互

### 设备协议（Infrastructure/Communication）

| 协议           | 实现状态     | 连接/超时 / 重连                                                                                                                                                                         |
| ------------ | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S7 (Siemens) | ✅ **完整** | `ConnectAsync` 建 `Plc`、读取/写入超时、`_plcLock` 状态复检；`ParseAddress` 解析 Area/DB/Byte/Bit，按 `DataTypeEnum` 校验并取位/字节；`WriteAsync` 校验地址与类型匹配；`DisposeAsync` 用 `Interlocked.Exchange` 保证只释放一次 |
| OPC UA       | ✅ **完整** | 建立会话（支持用户名密码）、`KeepAliveInterval=5000` + KeepAlive 回调自动重连；`AcquireSessionForIoAsync` 保证 IO 期间会话不被替换；Disconnecting 态阻断失效会话                                                          |
| Virtual      | ✅（模拟）    | 模拟值生成 / 缓存写入，用于无硬件的开发调试                                                                                                                                                            |
| MQTT         | ⚠️ 部分    | `MqttManager`（存在）做服务器管理与 `PublishVariableUpdateAsync`；但 Web 层注入的 `IMqttService`→`MqttHandler` **是占位实现**（`PublishAsync`/`SubscribeAsync` 直接 `Task.CompletedTask`）                   |
| MODBUSTCP    | ⛔ 未实现    | `ProtocolDriverFactory` 中 `MODBUSTCP`/`MQTT` DriverKey 直接抛 `NotSupportedException`                                                                                                 |

- **驱动派发**：`ProtocolDriverFactory.CreateDriver(driverKey)` 按 `Protocol.DriverKey` 选择驱动。新协议 = 库里加协议 + 工厂加分支，Runtime/前端零改动。

- **历史数据**（Infrastructure/Influx）：`InfluxStore` 写 `variable_history`，tags=`device_key,variable_key`，fields=`value/raw_value/quality/device_id/variable_name`；热重载客户端；`DeleteBeforeAsync` 定时清理。

- **实时推送**：SignalR 双 Hub `/hubs/scada`（`SubscribeDevice`/`UnsubscribeDevice` 按设备分组）、`/hubs/systemlog`（后台日志广播，仅广播 Category=Runtime 且非敏感的日志）。两 Hub 均 `[Authorize]`。

- **开放 API 网关**：`/open` 终端中间件匹配 `ExposedApiRegistry`，读变量实时值返回 JSON（**明文、匿名**，属刻意设计的对外集成入口）。

- **系统监控**：`SystemMonitorService`（PerformanceCounter）采集机器/进程指标。

***

## 8. 潜在问题与优化建议

### 必须修复（阻断 / 高危）

1. **数据库明文密码硬编码**（已确认）：`ScadaServer.WebApi/appsettings.json` 与 `appsettings.dboverride.json` 都明文写了 MySQL `root` 密码（`Pgw15221236646`）。`dboverride` 文件位于内容根目录，可能随打包/备份扩散；`.bin/Debug` 编译产物也拷贝了 `appsettings*.json`。\
   ➜ 生产必须移除明文，改用环境变量/密钥管理系统注入 `SystemDbConfig__Password`，并把 `dboverride`/密钥排除出发布包。
2. **JWT 密钥为占位硬编码**（已确认）：`appsettings.json` 的 `Jwt:Key` 是 `dev-only-jwt-signing-key-do-not-use-in-production-*`，且该默认值就在配置里。代码已做“缺失即抛”防护，但**只要不主动用** **`Jwt__Key`** **覆盖就会用这个公开占位密钥签名**（等于可伪造任意用户 Token）。\
   ➜ 生产必须用环境变量 `Jwt__Key` 覆盖，并设置足够强度的随机会话密钥；`ExpireHours=8` 可评估收紧。
3. **无权联合数据库开放接口**（设计权衡）：`/open/*` 网关分支挂载在认证之前，可匿名读取已启用变量的实时值。若这些变量含敏感工艺数据，属数据暴露面。\
   ➜ 至少对 `/open` 增加可选的白名单 key/签名校验，或对敏感变量禁止暴露。
4. **协议缺口导致运行时跳过**（边界）：配置 `MODBUSTCP`/`MQTT('DriverKey')` 时 `ProtocolDriverFactory` 抛 `NotSupportedException`；`RuntimeManager` 的建驱动/连接已 catch 成失败重连占位。若生产有计划变成现实会有未知行为。\
   ➜ 在 UI 层隐藏未实现协议，或明确“跳过+日志”，避免出现“配置了却一直 Fault”的困惑。

### 可选优化（非阻断）

- **Swagger 无条件开启**：`UseSwagger`/`UseSwaggerUI` 未按环境裁剪，生产也会暴露 API 文档。建议仅 `IsDevelopment()` 启用。

- **HTTPS 重定向被注释**：`UseHttpsRedirection()` 被注释，若走公网建议按部署环境开启或交给反向代理。

- **Modbus/MQTT 驱动占位**：`MqttHandler` 是空实现、`DeviceRegistry` 目前更像“注册缓存”未被读写驱动消费。可评估接入真实 MQTT 或移除冗余。

- **DTO 幂等/校验**：`AuthController.ChangePassword` 等对 `id` 解析失败只返回 Unauthorized，逻辑可接受但错误语义可更精确。

- **安全日志**：`SystemLog` 表中 `Content`/日志原文可能含查询串/脚本源码等，建议在入库/广播前对敏感关键字脱敏（当前广播已限 Category=Runtime）。

- **测试覆盖薄弱**：目前仅 S7 地址解析有单测，且 `Infrastructure.Tests` 未纳入解决方案执行。建议为 `RuntimeManager`（写校验）、`AlarmRuleEngine`（状态机）、`VariableBindingEngine`（环检测）补用例。

- **密码哈希方案未在本次确认**【缺少文件，无法确认】：`SystemUserAppService`/`LoginAsync` 具体散列与迭代次数未在本轮详读，建议自查 `SystemUsers` 密码字段存取。

***

## 9. 新手阅读顺序建议

想最快读懂整套项目，按下面的路径阅读（依赖关系从小到大）：

1. **`Server/Readme.md`（本文档）** + `docs/runtime-architecture.md` —— 全局视角与运行时架构。
2. **`ScadaServer.Domain/`** —— 先看 `Entities`（Device/DataModel/Protocol/DeviceVariable/ModelVariable）、`Enums`、`Interfaces/IProtocolDriver.cs`。这是“万物的事实源”。
3. **`ScadaServer.WebApi/Program.cs`** —— 启动流程、服务如何归置。
4. **`ScadaServer.WebApi/Extensions/*.Extensions.cs`** —— 组合根，看每个模块注入方式（单例/Scoped/HostedService）。
5. **`ScadaServer.Runtime/`** —— 核心中的核心：先 `RuntimeManager.cs`，再 `Devices/DeviceScheduler.cs` → `Devices/DeviceWorker.cs` → `Variables/VariableRuntime.cs` → `Events/VariableChangeBus.cs`。
6. **`ScadaServer.Infrastructure/Communication/`** —— 挑一个驱动读透（推荐 `S7Driver.cs`，含地址解析与状态锁），其余类比。
7. 然后按需看 **`Alarms/AlarmRuleEngine.cs`、`Bindings/VariableBindingEngine.cs`、`Scripting/`、`Tasks/`**。
8. 回看 **`ScadaServer.WebApi/HostedServices/`** 的 4 个 Recorder/快照服务，理解“采集不落库、异步批量落库”。
9. 最后看 **`Controllers/`（30 个）** 与 **`ScadaDbContext.cs`**（表/索引/约束）——理解对外 API 与持久化细节。

> 心里烙三句话即可：**变量身份 = DeviceKey+VariableKey；采集=Worker 轮询驱动；写库全走有界 Channel 异步批量。**

***

## 10. 后续可扩展方向（可选）与文档维护预留

### 可能的扩展方向（结合现状，仅建议）

- 补齐 **Modbus TCP / 真实 MQTT 驱动**，在 `ProtocolDriverFactory` 加分支并让 UI 与运行时对齐。

- `/open` 网关加 **鉴权签名 / API Key / IP 白名单 + 配额限流**，开放给第三方时可控。

- 引入 **消息分片/订阅持久化**：SignalR 目前纯下行；如需下行指令可靠投递可接队列（Redis Stream / RabbitMQ）。

- 历史库**降采样/压缩策略**已具备（StoreModeEnum），可扩展 Influx Flux 查询优化大范围趋势取数。

- **配置中心化与密钥托管**：接入 `.NET User Secrets` / 环境变量 / 专有密钥服务，替换明文配置。

- 为\*\*关键链路（写校验、报警状态机、绑定环检测、脚本授权）\*\*补齐单元测试并纳入解决方案。

### 文档维护预留接口

本文档基于你当前提供的文件生成。后续你可以：

- 继续提供**新增/修改的代码文件路径**，我会按以上章节逐节更新，保持版本连贯（建议在文件头维护“版本/更新时间”一行）。

- 或者提供**设计文档/需求文档**，我据此补充第 4/5 节的流程图与模块定位，并核对是否与代码一致。

- 遇到排查 bug 时，提供**报错日志 + 相关文件片段**，我会基于本文档的结构快速定位并更新风险清单。

***

## 附：已确认的关键实施细节备忘

- 全局默认授权 FallbackPolicy = 必须登录；`RequireAdmin` 用于组态/管理/暴露接口等管理类写接口。

- 设备连接失败进入**占位运行时 + NeedsReconnect**，调度器按 5s 退避自动重连，对外状态 `Fault`。

- 绑定写入回读用 `LastBindingWriteValue/Time` 回声抑制；报警带防抖+去重；脚本连续 3 次失败熔断。

- 历史默认优先 InfluxDB，失败回退 MySQL（有降级但数据一致性以日志为准）。

- 枚举/String 索引列均已显式 `HasMaxLength` 映射为 varchar，规避 Pomelo longtext 无法建索引的问题。

