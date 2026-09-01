# ScadaServer.WebApi 项目分析文档

> 本次分析基准：仓库 `d:\CSharp\SCADA\Server\ScadaServer.WebApi`，仅依据该目录已提供的代码与配置文件，不包含未读及的兄弟工程内部实现细节（涉及跨工程处已标注“需查源文件”）。
> 本文档采用 **文字化描述**，避免复杂绘图，方便单人维护、自查 Code Review 与排查 bug。
> 本文档可**增量维护**：后续新增/变更文件可继续追加到对应章节，并在文档顶部更新“版本记录”，保持版本连贯。

***

## 1. 项目概述

- **项目功能**：SCADA 上位机监控系统的**后端服务（Web API / 服务端入口工程）**。它对外提供 REST API 与 SignalR 实时推送通道，对内负责系统启动编排、设备采集运行时（Runtime）、历史/报警写入、系统日志与操作审计、开放 API 网关（`/open/*`）等核心生命周期的承载。

- **业务目标**：把下位机（PLC 等设备）采集到的数据，以"配置驱动"的方式接入系统 → 存入历史库（InfluxDB 为主）/ 实时库（MySQL）→ 通过 REST + SignalR 推送给浏览器组态客户端；并通过报警规则、脚本、定时任务、MQTT、开放接口等实现监控、联动、对外对接。

- **使用场景**：工业/流程监控上位机。单人（部分 AI 辅助）开发维护，面向局域网内组态客户端（前端 Vite 应用）访问。

- **关键说明**：`ScadaServer.WebApi` 是整套解决方案的**聚合与宿主层**，本身不包含 PLC 驱动、采集逻辑，而是通过引用 `Application`（业务服务）、`Domain`（实体/枚举）、`Infrastructure`（驱动/DB/Influx/MQTT）、`Runtime`（采集运行引擎）四个兄弟工程来组装实现。因此读懂 WebApi 的关键在于理解它如何"接线"这些工程。

***

## 2. 技术栈清单

**框架/语言**

- .NET 8 / C#（`net8.0`，可空引用类型 + 隐式 using 开启）

- ASP.NET Core Web（`Microsoft.NET.Sdk.Web`）

**NuGet 包（WebApi 工程直接引用，全部为框架级，无第三方业务包）**

- `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.0 —— JWT 认证

- `Microsoft.AspNetCore.OpenApi` 8.0.0

- `Swashbuckle.AspNetCore` 6.5.0 —— Swagger 文档

**间接引入（通过兄弟工程，具体版本在各自 csproj）**

- EF Core + Pomelo MySql —— MySQL ORM（经 `Infrastructure` 的 `ScadaDbContext`）

- InfluxDB 2.x 客户端（经 `Infrastructure.Influx.InfluxStore`）

- MQTT 客户端（经 `Infrastructure.Communication.MqttManager`）

- S7/OPC UA/Virtual 驱动（经 `Infrastructure.Communication`）

- SignalR（`AddSignalR`，WebApi 直接注册 Hub）

**外部依赖服务**

- MySQL 主库（业务实时数据，端口 3306）

- InfluxDB 时序历史库

- MQTT Broker（可选，用于对外发布设备实时值）

**通信协议**

- HTTP/HTTPS REST（`/api/*`）

- WebSocket SignalR（`/hubs/scada`、`/hubs/systemlog`）

- `/open/*` 开放 API 网关（自定义中间件）

- 设备下行：S7、OPC UA、以及占位的 MQTT/Virtual（在 Infrastructure 层实现）

**前端配套**（不在本工程，仅说明调用方）：`../Client` 下的 Vite + Vue3 应用，通过 Vite 代理将 `/api`、`/hubs` 转发到本服务端口 5555。

***

## 3. 目录结构解析

> 类别标注：**\[核心业务]** = 本仓库编写的核心逻辑；**\[编排/配置]** = 工程组织与 DI 装配；**\[中间件/过滤器]** = 横切关注点；**\[日志]** = 基础能力。

```
ScadaServer.WebApi/
├── Program.cs                          [核心业务] 入口：Host 构建、配置叠加、异常兜底、启动编排
├── appsettings.json                    [编排/配置] 主配置（DB/DBLog/JWT/CORS/HmiImage/Scripting/Devices）
├── ScadaServer.WebApi.csproj           [编排/配置] 工程文件，引用 4 个兄弟业务工程
├── ScadaServer.WebApi.http             [工具] HTTP 手测用例文件（VS 调试用）
│
├── Controllers/                        [核心业务] REST API 控制器（30 个，见 §6）
├── Extensions/                         [编排/配置] DI 装配 + 中间件管道（5 个扩展类，见 §6）
├── HostedServices/                     [核心业务] 后台托管服务（启动/运行时/写库/清理，见 §6）
├── Services/                           [核心业务] WebApi 专属服务（SignalR/MQTT 通知、审计、状态适配等）
├── Hubs/                               [核心业务] SignalR 实时通道（ScadaHub / SystemLogHub）
├── Middlewares/                        [核心业务] 全局异常 + /open 开放网关
├── Filters/                            [核心业务] 操作日志审计过滤器 [AuditLog]
├── Logging/                            [核心业务] ILogger 写库 Provider（DatabaseLoggerProvider）
└── Properties/launchSettings.json      [工具] 本地调试启动配置（http://0.0.0.0:5555）
```

**来源区分说明**：WebApi 工程内**全部为本仓库自有代码（业务 + 编排）**，无脚手架模板残留、无第三方库源码混入；唯一“框架属性”来自 ASP.NET Core 自身（控制器基类、Host、中间件、DI 等），不属于我们需要评审的业务代码。其余兄弟工程 `Application / Domain / Infrastructure / Runtime` 也均为自有业务代码。

***

## 4. 整体架构 & 分层说明

采用**经典分层 + 依赖倒置**，依赖方向单一：`WebApi → Application/Infrastructure/Runtime/Domain`，`Application → Domain/Infrastructure`，`Infrastructure → Domain`，不出现跨层反向依赖。

```
┌─────────────────────────────────────────────────────────────┐
│  ScadaServer.WebApi  (宿主/聚合层，本次分析对象)               │
│  · REST Controller → 调 Application.* AppService            │
│  · SignalR Hub / SignalRNotificationService → 下行推送       │
│  · HostedService → 编排启动、写库、清理、重连                   │
│  · Middleware → 异常兜底、/open 开放网关                       │
└───────────────┬─────────────────────────────────────────────┘
       依赖↓(只引用，不反向)
┌───────────────┼─────────────────────────────────────────────┐
│  Application 业务服务层  (DTO/AppService/接口)   → 定义用例     │
│  Infrastructure 基础设施层 (DB/Influx/MQTT/驱动/仓储)            │
│  Runtime 运行引擎层       (RuntimeManager/采集/报警/脚本/定时)   │
│  Domain 领域层            (实体/枚举/接口/常量/异常)             │
└─────────────────────────────────────────────────────────────┘
```

**各层职责与禁止事项**

- **WebApi（本层）**：仅做 HTTP 边界、认证授权、DI 装配、后台托管生命周期、实时推送。**不写业务规则、不直接操作 DbContext/驱动**（DB 访问统一经 `Infrastructure` 的 `ScadaDbContext` 与仓储，运行时状态经 `RuntimeManager`）。

- **Application**：业务用例编排，操作 DTO、调用仓储/UnitOfWork。不感知 HTTP。

- **Infrastructure**：具体技术实现（EF、Influx、S7/OPC/MQTT 驱动、仓储）。不依赖上层。

- **Runtime**：采集运行引擎（设备调度、变量绑定、报警引擎、脚本执行、定时任务）。通过接口被上层调用。

- **Domain**：纯领域模型，零依赖。

**禁止跨层逻辑**（约定类，需自查）：

- Controller 不得直接调用 Repository / DbContext（应经 AppService 或 RuntimeManager）。

- 业务服务不得引用 WebApi 类型（如 `IHttpContextAccessor`、`IHubContext`；WebApi 已刻意通过 `IScadaNotificationService` 等接口把推送能力注入下游，见 [SignalRNotificationService.cs](Services/SignalRNotificationService.cs) 的注释）。

- 时间戳一律使用 UTC（项目强约束），前端负责本地化展示。

***

## 5. 核心业务数据流

### 场景 A：设备实时数据采集 → 历史库 / 实时库 / 推送（最重要链路）

```
[Infrastructure 驱动轮询采集]  (S7/OPC/Virtual，由 Runtime.DeviceWorker 调度)
        │ 原始采样点
        ▼
[Runtime.RuntimeManager]  更新内存变量值，触发 变量变化事件
        │
        ├─▶ [WebApi.SignalRNotificationService]  → SignalR 只推订阅该设备的 Group(device-id)
        │        │                                  (ScadaHub.ReceiveVariableUpdate)
        │        └─▶ MQTT PublishVariableUpdate(质量有效且有值才发, 可选)
        │
        ├─▶ [WebApi.HistoryRecorder]  Record()非阻塞入队 → 后台每批100条/500ms
        │        └─▶ 优先 InfluxDB (IsConfigured) → 失败回退 MySQL VariableHistories
        │
        └─▶ [WebApi.RealtimeSnapshotService] Update()内存快照 → 每1s 全量 Upsert 到 MySQL VariableRealtime
```

- **告警链路**：变量变化 → `Runtime.AlarmRuleEngine` 判定 → `AlarmRecorder.Record()` 入队 → 批量写 `AlarmRecords`；同时 `SignalRNotificationService.NotifyAlarm` 广播 `ReceiveAlarm`。

- **系统日志链路**：`ILogger` → `DatabaseLoggerProvider` → `SystemLogRecorder.RecordRuntime`(有界) → 落库 SystemLogs + SignalR 广播非敏感运行日志；操作审计 → `OperationAuditService.RecordOperation`(无界) → 落库（不广播，防敏感字段外泄）。

### 场景 B：控制下发（前端写变量值）

```
前端 POST /api/Device/{id}/variables/{key}/write
   → [Authorize Roles=Operator,Admin] + [AuditLog("变量写入","WRITE")]
   → DeviceController.WriteVariable
   → IDeviceAppService.WriteVariableAsync
   → RuntimeManager(RuntimeDevice.Variables) 驱动 WriteAsync 物理下发
   → 结果经 SignalR 广播给订阅客户端可见
```

### 场景 C：外部系统读设备值（开放 API 网关）

```
GET /open/<RouteUrl>
   → [WebApplicationExtensions] Map("/open") → ExposedApiMiddleware (终端处理器,不鉴权)
   → IExposedApiRegistry.TryMatch(method,path) 命中启用配置
   → 从 RuntimeManager.DeviceRuntimes 取设备→变量最新值 → 统一 JSON 返回(UTC)
```

### 场景 D：系统启动编排

```
Program.cs 构建 Host → AddAuthenticationServices / AddDatabaseServices / AddApplicationServices / AddInfrastructureServices
   → 注册 HostedService: Startup → Runtime → SystemLogCleanup → AlarmRecordCleanup → ScriptExecutionRecordCleanup
   → StartupHostedService: ①数据库迁移+种子(必须成功,失败抛宿主停止) → _dbReady.MarkSucceeded；②MQTT启动(允许失败,靠自动重连)
   → RuntimeHostedService: 等待 _dbReady.WaitAsync → RuntimeManager.InitializeAsync → StartAsync
   → 各 Recorder/清理服务同样先 await _dbReady.WaitAsync 再查询，避免迁移前误写
```

***

## 6. 核心入口文件、关键类说明

### 6.1 启动入口：`Program.cs`

- 手工拆分 `StartAsync` + `WaitForShutdownAsync`（不用 `RunAsync`），规避启动异常时 Host 提前 Dispose 导致 EventLog 二次崩溃。

- 叠加 `appsettings.dboverride.json`（主库连接热改后需重启生效，注释明确）。

- 全局异常兜底：`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`，均用 `SafeLog` 保证兜底自身不抛。

- 端口占用预检（仅提示，最终以 Kestrel 绑定为准），失败优雅 `StopAsync` 后返回非零退出码。

### 6.2 DI 装配（`Extensions/`）

- `Authentication.Extensions.cs`：CORS 白名单、JWT（缺 `Jwt:Key` 快速失败）、全局 `FallbackPolicy=RequireAuthenticatedUser`（防漏 `[Authorize]` 裸奔）、`RequireAdmin` 策略、SignalR access\_token 查询参数鉴权、枚举/CLR 类型 JSON 转换器。

- `Database.Extensions.cs`：EF Core `ScadaDbContext`(Scoped) + `IUnitOfWork` + 全部仓储注册。

- `Application.Extensions.cs`：注册各 AppService(Scoped) + 多个单例 Hosted 写库服务（History/Alarm/SystemLog/RealtimeSnapshot）、`IExposedApiRegistry`、`IMqttManager`、`SignalRNotificationService`、审计服务、状态订阅者。

- `Infrastructure.Extensions.cs`：设备注册/协议工厂/InfluxStore/历史迁移/`RuntimeManager`/绑定引擎/报警引擎/脚本引擎/定时调度器/MQTT（均 Singleton）+ `SystemMonitorService`。

- `WebApplicationExtensions.cs`：中间件管道（CORS → Exception → Swagger → Map(/open) → Routing → Auth → MapControllers + 两个 Hub）。

### 6.3 后台托管服务（`HostedServices/`）

- `StartupHostedService` / `RuntimeHostedService`：启动编排，见 §5 场景 D。

- `DatabaseInitializationStatus`：基于 TCS 的启动就绪协调（不用 Thread.Sleep），`WaitAsync` 不抛异常（取消/失败返回结果）。

- `HistoryRecorder` / `AlarmRecorder` / `SystemLogRecorder` / `RealtimeSnapshotService`：4 个“非阻塞入队 + 后台批量落库”的常驻写库器（有界 DropWrite 通道 + 百条/500ms 批量）。

- `MqttReconnectHostedService`：每 15s 扫描未连接 MQTT 并触发重连。

- `SystemLogCleanupHostedService` / `AlarmRecordCleanupHostedService` / `ScriptExecutionRecordCleanupHostedService`：每天 3 点按保留期分批删（LIMIT 2000 + 延迟，防长锁）。

### 6.4 控制器（30 个，`[Route("api/[controller]")]` 或固定前缀）

- 配置/管理类（类级 `[Authorize(Policy="RequireAdmin")]`）：Area、DataModel、DeviceVariable、Protocol、MqttServer、MqttVariableConfig、DataConversion、DatabaseConfig、ModelVariable、ScheduledTask、ExposedInterface、SystemConfig、SystemUser、SystemScript、AlarmRule、LinkageRule、ScadaProject、ScadaPage、HmiComponent、HmiImage、SystemLog 等。

- 运行/只读类（全局认证即可，个别放行 Operator/Admin）：Device（写变量 `[Authorize(Roles=Operator,Admin)]`）、ScriptRuntime（`[Authorize(Roles=Operator,Admin)]`）、History、TelemetryData 等。

- 匿名端点：`AuthController.Login`（`[AllowAnonymous]`）、`HmiImageController` 的图片读取（`[AllowAnonymous]`，供运行端画面加载）。

### 6.5 关键横切类

- `ExceptionMiddleware`：统一异常响应；`BusinessException` 透传状态码，其余 500；仅开发环境回显堆栈。

- `ExposedApiMiddleware`：终端处理器，动态路由匹配 `IExposedApiRegistry`。

- `AuditLogAttribute` / `AuditLogActionFilter`：写请求自动审计，按结果分 Information/Warning/Error 三档。

- `DatabaseLoggerProvider`：ILogger 写库 Provider（惰性解析 `SystemLogRecorder` 破循环依赖，前缀/全名黑名单防递归）。

- `SignalRNotificationService`、`OperationAuditService`、`VariableWriteAuditRecorder`、`DeviceStatusPersistenceSubscriber`、`RuntimeStatusProviderAdapter`：见 §6 之上/之间引用。

***

## 7. 外部依赖 & 第三方交互

| 依赖                     | 用途   | 通信方式                      | 超时/重连                                                   |
| ---------------------- | ---- | ------------------------- | ------------------------------------------------------- |
| MySQL(3306)            | 主业务库 | EF Core + Pomelo          | `EnableRetryOnFailure(3次,5s延迟)`；启动迁移必须成功                |
| InfluxDB 2.x           | 历史时序 | `InfluxStore`             | 单例热切换；写入失败**回退 MySQL**                                  |
| MQTT Broker            | 对外发布 | `MqttManager`             | 启动允许失败；`MqttReconnectHostedService` 每 15s 重连扫描          |
| PLC(S7/OPC UA/Virtual) | 采集   | 各 Driver                  | 驱动层各自 Connect/Read/Write 容错；`RuntimeManager` 设备级失败跳过并日志 |
| 浏览器客户端                 | 人机交互 | REST + SignalR(WebSocket) | JWT 8h；SignalR 经 access\_token 鉴权                       |

**开放对外（`/open/*`）**：不鉴权，供第三方只读轮询设备值——属于刻意的“数据暴露接口”，但需注意其**无认证、无限流**（见 §8）。

其他说明：`ScadaHub` Capabilities 仅 `SubscribeDevice/UnsubscribeDevice`，为纯下行推送通道（前端引用计数管理分组）；`SystemLogHub` 为空 Hub，仅用于服务端主动推送。

***

## 8. 潜在问题与优化建议

> 风险清单按严重度分两档：【必须修复阻断问题】与【可选优化】。均基于当前已读代码，未读部分标注【缺少文件，无法确认】。

### 8.1 必须修复 / 阻断问题

1. **明文凭据进仓库（高危·安全）**
   `appsettings.json` 中含明文数据库密码 `SystemDbConfig.Password` 与开发 JWT 密钥 `Jwt.Key`。虽代码已注明生产应经环境变量覆盖，但**仓库内仍存在明文凭据**，一旦仓库泄露即拖库/伪造 Token。建议：改用环境变量/用户密钥，仓库内仅放占位符，并把 `.gitignore` 纳入该类文件、检查是否误提交至历史。

2. **`DeviceStatusPersistenceSubscriber`** **使用** **`async void`** **事件处理器（中高·异常/资源）**
   `_runtimeManager.StatusChanged += OnStatusChanged` 且 `OnStatusChanged` 为 `async void`（见 [DeviceStatusPersistenceSubscriber.cs](Services/DeviceStatusPersistenceSubscriber.cs#L32)）。异常无法被上游捕获，仅落 `Console.Error`，丢失日志上下文与诊断信息。建议改为 `async Task` + 在事件内 try/catch 并用 `ILogger`，或改由专门的阻塞通道消费状态事件。

3. **`/open/*`** **开放网关无认证、无限流、单点可读（中·安全边界）**
   该网关不鉴权即放行设备值，虽属产品意图，但**不可同时泄露内网设备全貌**。建议至少加：可选的访问令牌/来源 IP 白名单、限流、以及最小化暴露字段的审计。

### 8.2 可选优化

1. **三个清理服务高度重复**（SystemLog/AlarmRecord/ScriptExecutionRecord 的 Cleanup `while`+`LIMIT` 循环几乎一致）：建议抽公共分批删除工具方法，降低维护成本。
2. **`RealtimeSnapshotService`** **每秒全量 Upsert 全部变量，无变化检测**；若变量量大，可比较内存前后值只写变化行。且 `_snapshots` 字典在变量删除后不会清理对应键（长期运行可能累计陈旧键）【需结合变量增删流程确认是否真会残留】。
3. **操作日志/安全日志使用无界通道**（`_operationChannel`），若数据库长期不可用消费端停摆，队列可能内存增长。可评估有界化并为审计提供降级（当前设计以“审计不丢”优先，属权衡）。
4. **JWT 无刷新令牌机制**：8 小时过期后需重新登录；单人局域网可接受，如扩场地部署建议补 slide/refresh 或延长有效期策略。
5. **`OperationAuditService`** **信任** **`X-Forwarded-For`** 取客户端 IP：部署在不可信反代后有伪造可能，建议仅在受信反代后启用。
6. **`MqttReconnectHostedService`** **固定 15s 无指数退避**：对故障 Broker 会有固定频率空转。
7. **`ExceptionMiddleware`** **开发环境回显** **`exception.Message`** **与堆栈**：需确保生产永远 `Development` 不开启，否则信息泄露。
8. **文档/注释过时**：`SystemLogHub` 注释仍称 `ScadaHub` 为 `[AllowAnonymous]`，而 `ScadaHub` 实际已加 `[Authorize]`，建议更新避免误导。
9. **重复的时间处理约定**：多服务各自用 `DateTime.UtcNow` 写库、`DateTime.Now` 判凌晨清理——代码已注释澄清，但属易错的边界点，建议全局统一 UTC 并收敛为辅助方法。【部分为约定，需自查一致性】

***

## 9. 新手阅读顺序建议

1. **`Program.cs`** —— 先看整条启动/关闭编排与配置叠加，建立全局印象。
2. **`appsettings.json`** —— 理解各配置节（DB/日志/JWT/CORS/HmiImage/Scripting/Devices）与注释蕴含的约束。
3. *`Extensions/*.cs`（4 个 Add* + WebApplicationExtensions）\* —— 看 DI 如何“接线”各兄弟工程与中间件管道，这是理解整套依赖的钥匙。
4. **`HostedServices/StartupHostedService`** **+** **`RuntimeHostedService`** **+** **`DatabaseInitializationStatus`** —— 看启动顺序与就绪协调。
5. **`HostedServices/HistoryRecorder`** **+** **`AlarmRecorder`** **+** **`RealtimeSnapshotService`** **+** **`SystemLogRecorder`** —— 看“采集怎么写库/推送”，理解核心数据链路。
6. **`Middlewares/ExceptionMiddleware`** **+** **`ExposedApiMiddleware`** 与 **`Services/SignalRNotificationService`** —— 看异常兜底与实时/开放输出。
7. **按需细读**：安全相关看 `Extensions/Authentication.Extensions.cs` + `Controllers/AuthController.cs`；审计看 `Filters/AuditLogAttribute.cs` + `Services/OperationAuditService.cs`；日志落库看 `Logging/DatabaseLoggerProvider.cs`。
8. 之后若需深入采集/报警/脚本，切到兄弟工程：`Infrastructure/Communication/*`（驱动）、`Runtime/RuntimeManager.cs` 与 `Runtime/Devices|Bindings|Alarms|Scripting|Tasks/*`。

***

## 10. 后续可扩展方向（可选）

- **开放 API 网关**：扩展为写接口（下发控制）、Bearer 访问令牌、限流与用量审计，形成安全的对外数据服务。

- **采集能力**：接入更多 PLC 协议、支持 OPC UA 服务器模式、增加设备级采集频率/启停的热配置（当前配置变更需重启服务，见项目约束）。

- **高可用**：多实例部署配合外部 Redis 做实时快照/锁/状态共享；当前为单实例内存态（`RuntimeManager`/快照均在进程内），扩展需重构。

- **可观测性**：引入结构化指标（采集点数/成功率/队列丢弃计数，多数已通过计数器存储但未暴露），可加 Prometheus 端点或把这些计数器暴露为状态接口。

- **历史归档**：结合 InfluxDB 降采样/保留策略自动归档，分担 MySQL 实时库压力。

- **操作/安全管理**：补 JWT 刷新、账号锁定/策略、细粒度 RBAC（当前仅 Admin/Operator/Viewer 级别）。

***

*版本记录*

- v1.0（2026-09-01）：首版，基于 ScadaServer.WebApi 现有代码与配置梳理。

