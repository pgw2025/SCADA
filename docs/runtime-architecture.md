# ScadaServer.Runtime 架构设计

> 分析对象：`Server/ScadaServer.Runtime/`（SCADA 运行期核心，.NET 8，`Nullable enable`）
> 定位：连接「配置/持久化层」与「物理设备/前端」的实时运行引擎。本层不负责持久化写入（除实时快照/历史/报警的异步落库通道外），而是消费 Domain 抽象、驱动设备、产出实时遥测、报警、联动与脚本执行。

---

## 1. 在分层架构中的位置

```
┌──────────────────────────────────────────────────────────────────┐
│  ScadaServer.WebApi  (宿主 / REST / SignalR Hub / 控制器 / DI 组合根) │
└───────────────┬──────────────────────────────────────────────────┘
                │ 注入 (Singleton)
┌───────────────▼──────────────────────────────────────────────────┐
│  ScadaServer.Runtime  ← 本文档 ★                                   │
│  RuntimeManager / Scheduler / Worker / Alarms / Bindings /          │
│  Scripting / Tasks / Events                                        │
└───────┬───────────────────────┬──────────────────────────────────┘
        │ 依赖（消费接口）         │ 依赖（消费接口）
┌───────▼──────────┐   ┌─────────▼──────────┐   ┌────────────────────┐
│ ScadaServer.     │   │ ScadaServer.        │   │ ScadaServer.        │
│ Application      │   │ Infrastructure      │   │ Domain              │
│ (I* 服务契约、    │   │ (DeviceRegistry、    │   │ (IRuntimeDevice/    │
│  DTO、Recorder、 │   │  ProtocolDriverFactory│  │  IRuntimeVariable/   │
│  Notification)   │   │  IInfluxStore、EF)  │   │  IProtocolDriver、   │
└──────────────────┘   └────────────────────┘   │  枚举)              │
                                                └────────────────────┘
```

**依赖方向（单向，无环）：**
- Runtime → **Domain**：仅依赖领域抽象接口（`IRuntimeDevice` / `IRuntimeVariable` / `IProtocolDriver` / `IProtocolDriverFactory`，及枚举）。这是关键解耦——**驱动不允许感知 `DataModel` / `ModelVariable` 等模板实体**。
- Runtime → **Application**：消费 `IRuntimeDeviceManager` / `IScadaNotificationService` / `IHistoryRecorder` / `IRealtimeSnapshotService` / `IAlarmRecorder` / `IVariableWriteAuditRecorder` 等契约。
- Runtime → **Infrastructure**：通过 DI 拿 `DeviceRegistry` / `ProtocolDriverFactory` / `IInfluxStore` / `ScadaDbContext` / 仓储。
- 反向不成立：Application / Infrastructure / Domain 均不引用 Runtime 的具体类型（仅通过接口与泛型 `IEnumerable<IScheduledTaskExecutor>` 等反向解耦）。特别注意 **RuntimeManager 主动调用 `IScadaNotificationService`**，而通知服务实现**不回注** `IRuntimeManager`，以此规避 Singleton 循环依赖。

**项目引用（csproj）：**
- `ProjectReference`：ScadaServer.Application、ScadaServer.Infrastructure
- `PackageReference`：**Jint 3.1.3**（JS 沙箱）、**Cronos 0.13.0**（Cron 解析）

---

## 2. 模块划分（目录即边界）

| 目录 | 职责 | 关键类型 |
|---|---|---|
| `.` | 运行时总控、设备生命周期、变量写入 | `RuntimeManager`、`Interface/IRuntimeManager` |
| `Devices/` | 设备运行时对象、单设备采集 Worker、调度派发 | `DeviceRuntime`、`DeviceWorker`、`DeviceScheduler` |
| `Variables/` | 变量运行时对象（模板+实例解析结果） | `VariableRuntime` |
| `Events/` | 进程内变量变化事件总线 | `VariableChangeEvent`、`IVariableChangeBus`/`VariableChangeBus` |
| `Alarms/` | 报警规则引擎（快照+热重载） | `IAlarmRuleEngine`/`AlarmRuleEngine`、`AlarmRuleSnapshot` |
| `Bindings/` | 变量绑定（OnChange 转发、环检测、回声抑制） | `IVariableBindingEngine`/`VariableBindingEngine` |
| `Scripting/` | JS 沙箱脚本引擎（调度/沙箱/授权/熔断） | `ScriptEngineHost`、`ScriptSandbox`、`ScriptRuntimeAccess` |
| `Tasks/` | 定时任务调度（Cron）+ 策略执行器 | `ScheduledTaskScheduler`、`IScheduledTaskExecutor` 系列 |

---

## 3. 核心抽象与契约

### 3.1 领域只读视图（Domain.Interfaces）
- **`IRuntimeDevice`**：驱动连接时唯一可见的设备视图（`Id` / `Key` / `ConfigJson` / `Variables`）。`ConfigJson` 来自 `DeviceConfig.JsonConfig`，驱动自行反序列化为协议配置（S7 的 IP/Rack/Slot、OPC UA 的 EndpointUrl 等）。
- **`IRuntimeVariable`**：驱动读写的变量视图。地址 `Address` 的**唯一权威来源是 `DeviceVariable.Address`**（设备实例级），驱动**禁止回退到模板 `ModelVariable.Address`**；轮询间隔、缩放、位偏移、死区、只读/启用均「实例优先、模板兜底」。

### 3.2 运行时读写契约（Application.Interfaces）
- **`IRuntimeDeviceManager`**：运行期热注册/注销/重载单台设备（`RegisterDeviceAsync` / `RemoveDeviceAsync` / `ReloadDeviceAsync`），以及 `WriteVariableAsync(deviceId, variableKey, value, writeSource)`。所有方法**不抛异常**，结果以 `(bool Success, string? ErrorMessage)` 或静默日志返回，避免业务写操作被运行期异常误判。
- **`IScadaNotificationService`**：实时推送（`NotifyVariableUpdateAsync` 带质量/时间、`NotifyDeviceStatusAsync`、`NotifyAlarmAsync`、`NotifySystemAlarmAsync`、`NotifyScriptExecutionAsync`）。实现方为 SignalR / MQTT，对 RuntimeManager 而言是下游黑盒。
- **`IRuntimeManager.TryGetRuntimeSnapshot`**（阶段 3）：聚合快照查询（连接态 + 运行态 + 报警 + 通信统计，含进程级 `ReconnectCount`）；`Status` 与 `ConnectionState` 同源同值、`RunState`/`HasAlarm` 正交维度一并返回。
- **`IHistoryRecorder` / `IRealtimeSnapshotService` / `IAlarmRecorder`**：均为**非阻塞入队、后台批量落库**模式（Channel/队列），绝不阻塞采集循环。

### 3.3 协议驱动契约（Domain.Interfaces）
- **`IProtocolDriver`**（`IAsyncDisposable`）：`ConnectAsync` / `ReadAsync` / `ReadBatchAsync` / `WriteAsync` / `SubscribeAsync` / `UnsubscribeAsync` / `DisconnectAsync`。`ReadAsync` 返回 `null` 表示本次无效（如虚拟设备未连接、订阅型暂无数据），由调用方按通信错误处理。
- **`IProtocolDriverFactory.CreateDriver(driverKey)`**：按 `Protocol.DriverKey` 派发驱动（S7 / OPCUA / VIRTUAL 已实现；MODBUSTCP / MQTT 抛 `NotSupportedException`）。**新增协议 = 库里加一条 Protocol + 工厂加一个分支，运行时与前端零改动**。

---

## 4. 启动与生命周期

```
应用启动
  └─ RuntimeHostedService.ExecuteAsync (BackgroundService)
       1. 等待 DatabaseInitializationStatus (TCS) —— 确保迁移/种子完成后再查库
       2. RuntimeManager.InitializeAsync()
            ├─ 建独立 Scope → 查 Devices（Include Model→Protocol, Config, DeviceVariables→ModelVariable）
            │   仅 IsEnabled 设备
            ├─ 逐设备 BuildAndRegisterDeviceAsync：
            │     ├─ 解析 Protocol.DriverKey → 工厂建驱动 → driver.ConnectAsync(runtime 视图)
            │     ├─ 成功：ConnectionState=Connected，构建 VariableRuntime 映射
            │     └─ 失败：注册「占位运行时」并 NeedsReconnect=true（对外 Fault）
            ├─ alarmRuleEngine.ReloadAsync()（同步加载报警规则快照）
            └─ bindingEngine.LoadAsync()（所有设备就绪后再建绑定索引）
       3. RuntimeManager.StartAsync(token)
            ├─ new DeviceScheduler(this, …).StartAsync(token)  —— 启动派发循环
            └─ bindingEngine.StartAsync(token)                 —— 启动转发消费循环
```

**关闭（`RuntimeHostedService.StopAsync` → `RuntimeManager.StopAsync`）：**
1. 先停绑定转发循环并排空（`bindingEngine.StopAsync`）；
2. 停调度器（`scheduler.StopAsync()` 等待所有 Worker 收尾，5s 超时兜底）；
3. 逐设备 `Driver.DisposeAsync()` 释放连接；
4. `bindingEngine.Clear()` 清索引。

> **生命周期边界铁律**：`RuntimeManager` 为 **Singleton**，但其注入的 `ScadaDbContext` 是 **Scoped**。故所有需要查库的地方都通过 `IServiceScopeFactory.CreateScope()` 建独立 Scope，绝不长期持有 Scoped 实例。

---

## 5. 数据采集管线（核心数据流）

```
                         DeviceScheduler (每 50ms tick)
                                  │ 仅对「无活跃 Worker 且过退避窗口」的设备派发
                                  ▼
                         DispatchWorker (DispatchSync 锁内原子：校验在册→置 IsRunning→建 workerCts→记录 Task)
                                  │
                                  ▼
                    DeviceWorker.WorkerAsync (单设备常驻循环)
          ┌───────────────────────────────────────────────────────┐
          │ 每 tick：                                              │
          │   now=UTC；收集 NextPollTime<=now 的到期变量            │
          │   for each 到期 vr:                                    │
          │     val = driver.ReadAsync(vr)                         │
          │     ├─ null → Quality=CommunicationError（质量降级推送1次）│
          │     └─ 成功 → Lock 内更新 Value/Previous/Quality/IsChanged│
          │            （回声抑制：绑定回显不触发变化事件）            │
          │     ├─ 发布 VariableChangeEvent → 事件总线              │
          │     ├─ 推送 SignalR 通知（fire-and-forget 后台任务）    │
          │     ├─ TryRecordHistory → IHistoryRecorder（异步入队）  │
          │     ├─ TryUpdateRealtime → IRealtimeSnapshotService     │
          │     └─ TryCheckAlarm → 规则引擎 / MinMax 兜底           │
          │   轮次级判定：anySuccess→Connected；全失败计数++         │
          │     连续 3 轮失败 → NeedsReconnect=true，break 退出      │
          └───────────────────────────────────────────────────────┘
                                  │
          调度器发现 NeedsReconnect → 退避 5s → RuntimeManager.ReconnectDeviceAsync
          （停旧 Worker/释放旧驱动 → 重新走完整注册流程，形成自愈循环）
```

**关键质量/变化模型（`VariableRuntime`）：**
- `ConnectionState`（Connected/Connecting/Error/Disconnected…）→ 对外 `DeviceStatus`（Online/Connecting/Fault/Offline）映射，仅在**对外状态值变化**时触发 `StatusChanged`，抑制抖动。
- `Quality`（Good/CommunicationError/…）：驱动返回 `null` 或异常即降级，前端据此标记「通讯异常」而非继续展示旧「僵尸值」。
- `IsChanged`：与上一值不等且非绑定回显；驱动「写后回读」的回声由 `LastBindingWriteValue` + 时间窗抑制。
- **历史存储策略（`StoreModeEnum`）**：None / Change（变化+死区抑制+超时兜底种子点）/ Cycle / Compressed / Aggregated（按 `StoreIntervalMs`）。首采必写「种子点」避免曲线断档。

---

## 6. 子系统详解

### 6.1 报警引擎（`Alarms`）
- **`AlarmRuleEngine`（Singleton）**：30s 定时下拉活跃规则 → 不可变快照 `AlarmRuleSnapshot` 整体替换（引用替换原子，`lock` 串行写），采集线程并发读安全。CRUD 后 `ReloadAsync` 即时生效。
- **求值权威**：`DeviceWorker.TryCheckAlarm` 先查规则，命中则规则为权威（带**防抖 `DebounceSeconds` + 去重状态机** `AlarmRuleState`）；无规则则回退 `ModelVariable.Min/Max` 兜底（来源标记 `MinMaxLimit`）。
- 规则热更新后 `PruneStaleRuleStates` 清理已删规则残留状态，避免旧状态永久屏蔽触发。
- 命中/恢复事件经 `NotifyAlarmAsync` + `IAlarmRecorder`（异步落库）。

### 6.2 变量绑定引擎（`Bindings`）
- 订阅 `IVariableChangeBus`，将源变量变化**转发写入目标变量**（单向/多跳 OnChange 联动）。
- **加载期环检测**：自环 + 多节点环用 DFS 标记并拒绝（推送系统报警）。
- **回声加固**：转发写入后记录 `LastBindingWriteValue/Time`，目标变量回读时抑制回显事件。
- **并发模型**：事件回调只做非阻塞入队到有界 `Channel`（`DropOldest` 背压，采集线程永不阻塞）；单消费者循环串行写，天然限流+保序。

### 6.3 脚本引擎（`Scripting`）
- **宿主 `ScriptEngineHost`（Singleton + `IHostedService`）**：250ms Tick 统一驱动 Periodic/Cron；OnChange 由订阅事件总线驱动。
- **沙箱 `ScriptSandbox`（Jint）**：仅暴露白名单 `read/getQuality/write/log`；`Strict(true)` + `LimitRecursion(100)` + 超时（500–30000ms 钳制）。`write` 受 `ScopeWrite`（`设备键.变量键`）精确授权，空=拒绝全部；`read` 受 `ScopeRead` 授权。
- **试运行 `TestAsync`**：`dryRun` 只记「将要写入」不落地、不更新熔断、不落库。
- **熔断**：连续 3 次失败（错误/超时）置 `Tripped`，自动触发跳过；手动 `RunAsync` 绕过熔断。
- **防重入**：同一脚本上一次执行未结束则本次标记 `Skipped`（不计入熔断）。
- 执行结果经 `NotifyScriptExecutionAsync` 推送 + `IScriptExecutionRecordRepository` 落库。

### 6.4 定时任务（`Tasks`）
- **`ScheduledTaskScheduler`（Singleton + `IHostedService`）**：1s Tick 驱动 Cron；兼容 6 段秒级 / 5 段分钟级；每 10 分钟兜底全量重载。
- **策略模式 `IScheduledTaskExecutor`**：按 `ScheduledTask.Type` 分派（SetValue / ExecuteScript / Backup / ClearHistory），DI 注入 `IEnumerable<IScheduledTaskExecutor>`。
- 执行状态（Running/Success/Failed/Skipped + 耗时/错误/NextRunAt）**回写 `ScheduledTasks` 表**供前端轮询；手动 `RunAsync` 绕过 Cron 但仍防重入；单任务 30min 超时。

### 6.5 进程内事件总线（`Events`）
- `VariableChangeBus`（Singleton）：同步回调快照调用列表，**逐订阅者 try/catch**，单个订阅者失败不影响采集。事件源 `VariableChangeSource`（Polling / UserWrite / BindingWrite）供订阅者区分回声与去重。

### 6.6 设备运行状态与报警聚合（`Devices` / `Alarms`，阶段 2/3 落地）
- **双状态正交模型**（`DeviceRuntime`）：`ConnectionState`（PLC 通信态）↔ `RunState`（机器运行态）为两个**正交**维度。语义边界（P4）：`DeviceStatus.Fault` = **PLC 通信故障**（由 `MapConnectionStateToStatus` 由连接态派生）；`DeviceRunState.Fault` = **机器自身故障/急停**（人工置位）。`Online + Stopped`、`Fault连接 + Running机器` 均为合法组合。
- **HasAlarm 聚合链路**：`DeviceWorker.FireEvent` **单点打点**（规则报警 + MinMax 兜底全覆盖）→ `DeviceRuntime.ApplyAlarmDelta(±1)`（clamp 0）→ `ActiveAlarmCount` / 派生 `HasAlarm`。**自愈闭环**：runtime 重建（重连/重启/重载）时 `BuildAndRegisterDeviceAsync` 从 `AlarmRecords` 计数 `RecoveredAt IS NULL` 初始化 `ActiveAlarmCount`，消除「Worker 重建丢状态 → HasAlarm 卡 true/false」的幽灵窗口。
- **RunState 持久化链路**：`PUT /api/devices/{id}/runtime/runstate`（Operator/Admin + AuditLog）→ 写 `Devices.RunState / RunStateChangedAt` 列 → 运行时内存 `SetDeviceRunState` 即时生效 → 服务重启/设备启用时由 `RestoreRunState` 回读（维护状态不因重启丢失）。**刻意支持禁用设备置位**——维护常发生在停机设备，启用后自动恢复。
- **聚合快照端点**：`GET /api/devices/{id}/runtime`（D5-a：未注册→404）一次返回连接态 + 运行态 + 报警 + 通信统计（含进程级 `ReconnectCount`）。无变量值（D4-1，预留 includeValues 扩展位）。实时变化仍走 SignalR。

---

## 7. 并发与线程模型（设计亮点）

| 机制 | 用途 |
|---|---|
| `DeviceRuntime.DispatchSync`（`object` 锁） | 调度派发临界区（校验在册→置位→建 token→记 Task）与注销临界区串行化，消除「注销后仍被派发」竞态 |
| `DeviceRuntime.Lock`（`SemaphoreSlim(1,1)`） | 内存态值更新临界区（采集写、用户写、绑定写串行化），与 `DispatchSync` 相互独立 |
| `Channel<…>`（有界 `DropOldest`） | 绑定转发写入解耦事件回调与执行，背压丢旧不阻塞采集 |
| `CancellationTokenSource` 链接链 | 全局关停 token → 调度器 token → Worker 独立 token（支持单设备启停）；所有权校验防旧 Worker 误释放新 token |
| `ConcurrentDictionary` | `DeviceRuntimes` / `_retryAfter` / `_workerTasks` / 脚本 `_jobs` `_inflight` 等无锁并发容器 |
| 快照组装（P6 决策） | `TryGetRuntimeSnapshot` **无锁读取**多个内存字段，容忍字段间微小不一致（误差窗口 ≤ 一个采集轮次）；**禁止**为求一致性对 `DeviceRuntime.Lock` 加锁（会与采集循环互相阻塞） |
| 重连窗口快照回退（P4 修复） | 重连 connect 期间设备被移出 `DeviceRuntimes`（原子化重连、避免旧 Worker 残留），`TryGetRuntimeSnapshot` 回退读 `_reconnecting` 保留的占位运行时，保证断线诊断快照始终可查；连接完成新运行时权威接管 |
| 退避窗口（5s） | 重连与失败重派均带退避，防刷屏/风暴 |
| UTC 时间戳 | 采集时间统一 UTC，跨时区/时区变更无偏移 |

**容错铁律**：所有下游（通知/历史/实时/报警/审计/绑定/脚本）均为 fire-and-forget 或异步入队，单个失败仅 `LogWarning` 不向上抛；事件总线订阅者异常被隔离；调度/Worker 循环异常被 `finally`/`catch` 兜底，绝不让未观察异常击穿进程。

---

## 8. 依赖注入注册（`WebApi/Extensions/Infrastructure.Extensions.cs`）

| 类型 | 生命周期 | 说明 |
|---|---|---|
| `RuntimeManager` / `IRuntimeManager` / `IRuntimeDeviceManager` | Singleton | 运行期总控，组合根单例 |
| `IVariableChangeBus` → `VariableChangeBus` | Singleton | 进程内事件总线 |
| `IVariableBindingEngine` → `VariableBindingEngine` | Singleton | 由 RuntimeManager 启动/停止（非独立 HostedService） |
| `IAlarmRuleEngine` → `AlarmRuleEngine` | Singleton | 自启 30s 热重载定时器 |
| `ScriptEngineHost` / `IScriptEngineHost` | Singleton + HostedService | 宿主启动 |
| `ScheduledTaskScheduler` / `IScheduledTaskScheduler` | Singleton + HostedService | 宿主启动 |
| `IScheduledTaskExecutor` × 4 | Singleton | SetValue / ExecuteScript / Backup / ClearHistory |
| `DeviceRegistry` / `IProtocolDriverFactory` | Singleton | 设备注册表 / 驱动工厂 |
| 下游 Recorder/Snapshot/Alarm/Notification/Audit | Singleton + HostedService | 异步落库后台服务 |

> 启动顺序由 `RuntimeHostedService`（依赖 `DatabaseInitializationStatus` 协调）统一驱动 `InitializeAsync` + `StartAsync`。

---

## 9. 评价与可演进点

**已落实的良好设计：**
1. 协议驱动与模板实体的彻底解耦（接口隔离 + 地址解析下沉到 `VariableRuntime`）。
2. 单设备单 Worker + 退避自愈重连，消除重复采集/通知/历史入库。
3. 全链路非阻塞下游 + 回声抑制 + 死区 + 质量模型，保障采集循环不被拖垮、前端不刷屏。
4. 规则引擎快照整体替换、绑定环检测、脚本沙箱授权+熔断，安全与可用性兼顾。
5. 严格生命周期边界（Singleton/Runtime 不持有 Scoped DbContext，靠 `IServiceScopeFactory`）。

**待演进 / 风险点（阶段 4 已登记）：**
- ~~`DeviceRuntime` 内存状态实体与运行时层重名~~（已删除死代码，Domain 层不再有该实体）。
- **D2-B RunState 自动推导**（暂缓）：待 D2-A 人工置位稳定运行一个迭代后，新增 `DeviceRunStateMappings` 独立映射表（不动 DataPoint 模板），`DeviceWorker` 采集成功后评估（位置紧邻 `TryCheckAlarm`）。
- **快照 includeValues**（暂缓，D4-2）：`?includeValues=true` + 上限截断（>500 变量），DTO 已预留扩展位；启用时同步评估 `TelemetryDataController` / `ExposedApiMiddleware` 两处直接消费方（P5）迁移。
- **RunState 变更推送**（暂缓，D10-b）：复用 `ReceiveDeviceStatus` 消息体附加 `runState` 字段（向后兼容，旧前端忽略新字段）。
- **ReconnectCount 重置端点**（暂缓）：管理端点清零 `_reconnectStats`，本轮不做。
- **报警去重状态跨 Worker 重建**（暂缓，F6 既有行为）：重连后重复 Triggered 若成实际困扰，将 `_ruleStates/_alarmStates` 上移到 `DeviceRuntime` 或引入持久化状态源。
- `ProtocolDriverFactory` 中 **ModbusTcp / MQTT 仍为 `NotSupportedException`**——前端/配置已支持但运行时未实现。
- `WriteVariableAsync` 的限幅校验、绑定写入在设备锁外执行网络 IO，逻辑完备但需关注极端并发下的「先读后写」时序。
- `DeviceRegistry` 目前仅被「注册时缓存」使用，未看到被读写驱动消费的路径，存在轻量冗余。
- 脚本/任务调度均依赖「内存作业快照 + 定时兜底重载」，若 DB 在两次重载间不可用，作业短暂停留在旧状态。

> **备注（P7）**：`ConfigUpdating` 为**预留死值**——配置热更新期间可置位，当前无产生路径。

