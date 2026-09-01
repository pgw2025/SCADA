# SCADA 运行时项目（ScadaServer.Runtime）分析文档

> 生成日期：2026-09-01　|　分析对象：`Server/ScadaServer.Runtime`
> 方法：逐文件阅读实际代码，未提供的内容一律标注【缺少文件，无法确认】。
> 本文件为分析文档，未改动任何源码。

---

## 1. 项目概述

`ScadaServer.Runtime` 是整套 SCADA 系统的**运行时执行层**（也叫"采集引擎/运行内核"）。它不负责页面展示，也不负责把数据存进业务库（那是 WebApi/Application/Infrastructure 的职责），它的核心工作是：

1. **数据采集**：在系统启动后，为每一台启用的设备创建工作线程（Worker），按每个变量自己配置的轮询周期，定时去底层设备（PLC 等）读取变量值。
2. **数据外发**：把读到的最新值实时推送给前端（通过 SignalR / MQTT），并为后续查询落历史库 / 实时库。
3. **逻辑联动**：变量 A 变化后，按"变量绑定"规则自动把 A 的值转写到变量 B（例如一个设备的输出联动另一个设备的输入）。
4. **报警判定**：根据报警规则（或变量上下限兜底）判定越界，触发 / 恢复报警并向界面推送、落库。
5. **脚本自动化**：托管一段 JS 脚本（Jint 沙箱），支持按周期 / Cron / 变量变化来触发，脚本内可 `read/write` 运行中的变量，被称为"系统脚本"，常用来做复杂联动逻辑。
6. **定时任务**：按 Cron 定时执行各类运维操作（数据备份、历史清理、脚本执行、变量赋值）。

一句话：**Runtime 是"设备 ↔ 内存 ↔ 数据库/前端"之间的实时数据通道和自动化引擎**。它做的是系统里最耗时、最需要并发与可靠性设计的部分。

使用场景/定位：单人维护、部分代码由 AI 生成的.NET 8 工控项目。Runtime 是独立类库，被 WebApi 宿主通过几个 `IHostedService` 拉起，和 WebApi 处于同一进程。

---

## 2. 技术栈清单

**项目文件 `ScadaServer.Runtime.csproj`：**
- 目标框架：`.NET 8`（net8.0），`ImplicitUsings` + `Nullable` 开启。
- 项目引用：
  - `ScadaServer.Application`（应用层接口与 DTO，主要提供通知/记录/快照等接口）
  - `ScadaServer.Infrastructure`（驱动、设备注册表、InfluxDB、DbContext）
- NuGet 包（第三方）：
  - `Jint 3.1.3`：纯 C# 的 JavaScript 引擎，用于跑"系统脚本"。
  - `Cronos 0.13.0`：Cron 表达式解析/计算，用于"定时任务"和"脚本 Cron 调度"。

**运行期依赖的其它服务（由外部注册，Runtime 只消费接口）：**
- `ScadaDbContext`（EF Core / MySQL）：主业务库，加载设备/模型/变量/报警规则/脚本/任务配置。
- InfluxDB 2.x（`IInfluxStore`）：时序历史库，存变量历史采样点 + 备份导出。
- SignalR（`IScadaNotificationService` 实现是 `SignalRNotificationService`）：向前端实时推送变量更新 / 设备状态 / 报警 / 脚本执行事件。
- 通信协议驱动（`IProtocolDriverFactory` 创建）：当前实现为 `S7Driver`（西门子 PLC，S7netplus）、`OpcUaDriver`、`VirtualDriver`；`ModbusTcp` / `MQTT` 驱动未实现（创建即抛 `NotSupportedException`）。【协议驱动文件均在 Infrastructure 层，Runtime 只通过工厂/接口使用】

**第三方依赖生态概览**（驱动类在 Infrastructure 项目，非 Runtime 自身）：S7（S7netplus）、OPC UA、SignalR、EF Core(MySQL)、InfluxDB 2.x。

---

## 3. 目录结构解析

Runtime 项目按功能分包，全部是**核心业务代码**（无脚手架/静态资源/配置），每个子目录一个职责：

```
ScadaServer.Runtime/
├── RuntimeManager.cs                 ★ 总控：设备运行时生命周期、写变量入口
├── Interface/
│   └── IRuntimeManager.cs            总控接口 + 设备状态变更事件参数
├── Events/                           进程内"变量变化事件总线"（内部通信）
│   ├── IVariableChangeBus.cs
│   ├── VariableChangeBus.cs          总线实现（非阻塞、逐订阅者兜底）
│   └── VariableChangeEvent.cs        事件载荷 + Source 枚举(Polling/UserWrite/BindingWrite)
├── Devices/                          采集核心
│   ├── DeviceRuntime.cs              一台设备的运行时对象（IRuntimeDevice 实现）
│   ├── DeviceScheduler.cs            派发调度器：单设备单 Worker
│   └── DeviceWorker.cs               ★ 单设备采集主循环（读驱动/推通知/历史/实时/报警）
├── Variables/
│   └── VariableRuntime.cs            变量运行时对象（模板定义+设备实例配置聚合）
├── Bindings/                         变量绑定（联动转发）
│   ├── IVariableBindingEngine.cs
│   └── VariableBindingEngine.cs      ★ 订阅事件总线→有界队列→消费线程转发写入
├── Alarms/                           报警规则引擎
│   ├── IAlarmRuleEngine.cs           （含快照/hit 模型）
│   └── AlarmRuleEngine.cs            规则热重载快照，供 Worker 查规则求值
├── Scripting/                        系统脚本执行引擎
│   ├── IScriptEngineHost.cs
│   ├── ScriptEngineHost.cs           ★ 调度(Tick)+派发队列+专用消费线程+熔断+看门狗
│   ├── ScriptSandbox.cs              Jint 受限沙箱（白名单 read/write/getQuality/log）
│   └── ScriptRuntimeAccess.cs        脚本进出运行时的"桥"（读写变量，写桥有超时）
└── Tasks/                            定时任务调度
    ├── IScheduledTaskScheduler.cs
    ├── ScheduledTaskScheduler.cs     ★ Cron 调度循环 + 防重入 + 状态回写
    ├── IScheduledTaskExecutor.cs     执行器策略接口
    ├── BackupTaskExecutor.cs          数据备份（MySQL+Influx 打包 zip）
    ├── ClearHistoryTaskExecutor.cs    历史清理（按保留天数删 InfluxDB）
    ├── ExecuteScriptTaskExecutor.cs   调用脚本引擎执行脚本
    └── SetValueTaskExecutor.cs        向变量写固定值
```

**归类总结：**
- 核心业务：`RuntimeManager.cs`、`Devices/`、`Variables/`、`Bindings/`、`Alarms/`、`Scripting/`、`Tasks/`（全是自己写的业务）。
- 三方库调用点：仅 `Jsint`(Jint 沙箱) 与 `Cronos`（脚本/任务调度）；两者都是"自己代码里引用的第三方包"，不是脚手架。
- 配置/静态资源：本目录没有；配置项（超时/队列容量等）通过 `IConfiguration` 读取，键名见下文第 6、7 节。

> 说明：驱动(S7/OpcUa/Virtual)、传感器、MqttHandler 等在 `ScadaServer.Infrastructure` 里，不属于本篇对象，但有引用关系（见第 4 节）。

---

## 4. 整体架构 & 分层说明

本库位于 **Application / Infrastructure 之上、WebApi 之下**，是整个 SCADA 的"运行内核"。

分层职责与依赖方向（从下往上单向依赖）：

```
┌─────────────────────────────────────────────┐
│ WebApi（宿主）                                │
│  - RuntimeHostedService 启动 Runtime          │
│  - Infrastructure.Extensions 做 DI 注册        │
│  - 各 Controller 调用 `IRuntimeDeviceManager`  │
└───────────────┬─────────────────────────────┘
                │ 引用
┌───────────────▼─────────────────────────────┐
│ ScadaServer.Runtime（本文档对象）             │
│  - 采集/联动/报警/脚本/任务 的执行引擎          │
│  - 依赖 Application 接口 + Infrastructure 实现 │
└───────┬──────────────────────┬──────────────┘
        │ 接口                │ 实现/驱动
┌───────▼────────┐   ┌────────▼──────────────┐
│ Application    │   │ Infrastructure         │
│ (接口+DTO)     │   │ 驱动/DeviceRegistry/    │
│                │   │ Influx/ScadaDbContext   │
└────────────────┘   └────────────────────────┘
```

**各层职责 & 禁跨层的约定：**
- **Runtime 层（本层）**：只负责"跑起来"。它通过接口（`IScadaNotificationService`、`IHistoryRecorder`、`IAlarmRecorder`、`IRealtimeSnapshotService`、`IVariableWriteAuditRecorder`、`IInfluxStore` 等）输出数据，不直接落地 SQL；通过 `IProtocolDriverFactory`/`IProtocolDriver` 驱动设备，**不感知具体驱动内部实现**。
- **禁止跨层**：Runtime 不直接持有 Scoped 的 `ScadaDbContext`（Singleton 场景都通过 `IServiceScopeFactory` 新建 Scope 再解析）。驱动（Infrastructure）不允许知道 `DataModel`/`ModelVariable` 实体，Runtime 通过 `IRuntimeDevice`/`IRuntimeVariable` 只读视图把地址/位偏移等解析好交给驱动。
- **关键设计约束（代码中反复强调）**：
  - 变量 `Address` 的权威来源是 `DeviceVariable`（设备实例配置），不是模板 `ModelVariable.Address`；运行时严禁直接碰模板地址。
  - 项目时间戳一律 `DateTime.UtcNow`；前端显示时再本地化（历史库 Influx 会做 `ToUniversalTime`，跨时区不偏移）。
  - 多路写（设备采集 / 用户写 / 脚本写 / 绑定写）都用设备级 `SemaphoreSlim Lock(1,1)` 串行化内存值更新，但**驱动 I/O 在锁外执行**（避免耗时 I/O 阻塞采集）。

---

## 5. 核心业务数据流

下面用文字流程描述各典型场景，把模块串联起来。

### 5.1 启动与初始化（WebApi 拉起 Runtime）
1. `Infrastructure.Extensions.AddInfrastructureServices` 把 `RuntimeManager`（Singleton，同时以 `IRuntimeManager`、`IRuntimeDeviceManager` 暴露）、`VariableChangeBus`、`VariableBindingEngine`、`AlarmRuleEngine`、`ScriptEngineHost`、`ScheduledTaskScheduler`+4 个执行器、`RuntimeHostedService` 注册进 DI。
2. `RuntimeHostedService.ExecuteAsync`：先 `await _dbReady.WaitAsync()`（等数据库迁移完成），然后调用：
   - `RuntimeManager.InitializeAsync()`：从数据库 `Include` 加载所有 `IsEnabled` 设备及其 `DataModel(→Protocol)`、`DeviceConfig`、`DeviceVariable(→ModelVariable)`；对每台设备 `BuildAndRegisterDeviceAsync`（建 `DeviceRuntime`→工厂 `CreateDriver`→`driver.ConnectAsync`→连接成功则建 `VariableRuntime` 填充 `runtime.Variables[dv.Id]`→`RegisterDevice`→`DeviceRegistry.UpdateDevice`）。初始化结束时 `AlarmRuleEngine.ReloadAsync()` 预载报警规则、`VariableBindingEngine.LoadAsync()` 加载绑定索引。
   - `RuntimeManager.StartAsync(token)`：`DeviceScheduler.StartAsync` + `VariableBindingEngine.StartAsync`。
3. 脚本引擎与任务调度器作为独立 `IHostedService` 由宿主各自 `StartAsync`。

### 5.2 采集数据流（最核心，箭头=调用）
```
DeviceScheduler.SchedulerLoopAsync（每 50ms tick）
  └─ 逐设备检查：NeedsReconnect? → 重连 | 无活跃Worker? → DispatchWorker(设备, 取 token)  [单设备单Worker，DispatchSync锁]
       └─ Task.Run → DeviceWorker.RunWorkerAsync
            loop:
              now=UtcNow；收集 due（NextPollTime<=now 的启变量）
              每个 due 变量：
                Driver.ReadAsync(vr)                    → 读物理设备（无锁）
                runtime.Lock 内更新 vr.Value/IsChanged  → 与写路径串行化
                IsChanged 且非回显 → _changeBus.Publish(VariableChangeEvent, Source=Polling)   [供绑定/脚本OnChange用]
                通知判定(变化或质量恢复) → 收集进 notifications
                TryRecordHistory(按 StoreMode/间隔/死区) → IHistoryRecorder.Record（异步批量落库）
                TryUpdateRealtime                          → IRealtimeSnapshotService.Update（内存快照，定期 Upsert MySQL）
                TryCheckAlarm(due变量)                     → 规则权重>Min/Max兜底，命中则 FireEvent → NotifyAlarmAsync + AlarmRecorder.Record
              整轮统计 SuccessCount/ConsecutiveFailureCount；
              连续≥3轮全失败 → NeedsReconnect=true, break（转入自动重连）
              通知集合 → fire-and-forget PushNotificationsAsync → IScadaNotificationService.NotifyVariableUpdateAsync（SignalR）
```

### 5.3 写变量数据流（用户/脚本/绑定/任务四路汇聚到同一入口）
统一入口 **`RuntimeManager.WriteVariableAsync(deviceId, variableKey, value, writeSource)`**：
1. 前置校验：设备在运行 → 变量存在 → 未禁用 → 非只读 → 驱动就绪 → 设备已 Connected。
2. 服务端数值限幅：非 bool 值超出 `vr.Min/Max` 拒绝下发（前端 min/max 可绕过）。
3. `driver.WriteAsync(vr, value)` **在设备锁外**执行，且用 `_deviceWriteTimeoutMs`（配置 `Devices:WriteTimeoutMs`，默认 5000，收敛 500~60000）`WaitAsync` 兜底超时。
4. 写成功后在 `runtime.Lock` 内更新 `vr.Value/PreviousValue/UpdateTime/IsChanged=false`（防下轮重复广播）。
5. `NotifyVariableUpdateAsync`（SignalR 广播）→ `_changeBus.Publish(Source=UserWrite)` → `_realtimeSnapshot.Update` 即时刷新实时表。
6. 非 HTTP 来源（`writeSource` 非空）调用 `IVariableWriteAuditRecorder.RecordAsync` 落写入审计；HTTP 来源由 WebApi `[AuditLog]` 过滤器记，避免重复。

各写入来源：
- **前端用户**：Controller → `IRuntimeDeviceManager.WriteVariableAsync(..., writeSource=null)`。
- **变量绑定**：`VariableBindingEngine.OnVariableChanged`（总线回调，仅 Source≠BindingWrite）→ 有界 `Channel`(DropOldest, 容量10000) → 单消费者 `DispatchLoopAsync` → `WriteTargetAsync` → `RuntimeManager.WriteVariableAsync(..., "变量绑定")`；写成功后记录 `LastBindingWriteValue/Time` 供"回显抑制"。
- **脚本**：脚本 `write()` → `ScriptRuntimeAccess.Write` → 阻塞等待 `RuntimeManager.WriteVariableAsync(..., "系统脚本")`，带 `_writeBridgeTimeoutMs` 超时（超时→孤儿任务 + `ContinueWith` 观察最终结果）。
- **定时任务 SetValue**：`SetValueTaskExecutor` → 同上入口，`writeSource="计划任务"`。

### 5.4 脚本执行数据流
1. `ScriptEngineHost`（IHostedService）StartAsync：建有界派发队列（FullMode=Wait，队满 TryWrite 返回 false→入队方丢弃计数）、订阅 `_changeBus.VariableChanged`、`ReloadAsync` 拉取启用未熔断脚本、启动调度 Tick 循环（250ms）+ 启动 `_consumerCount`(默认2) 个 LongRunning 专用消费线程。
2. 触发来源：
   - **Periodic/Cron**：Tick 循环里 `job.NextUtc` 到期 → `TryEnqueue`。
   - **OnChange**：总线回调里按 `WatchDeviceKey+WatchVariableKey` 匹配 + 死区 + 冷却后 `TryEnqueue`（回调微秒级，脚本绝不内联在采集线程跑）。
   - **Manual/Test**（API）：`RunAsync/TestAsync` → 构造 `ScriptDispatchRequest`（带 `TaskCompletionSource`）→ `TryEnqueue` → `WaitAsync(120s)` 等结果。
3. 消费线程出队 → `DispatchAsync`：`_inflight.TryAdd` 防重入（忙则 Skipped）→ 熔断检查 → `new ScriptSandbox(code, 超时, access, dryRun, scopeRead, scopeWrite)` → `Run()` 或 `OnChange(payload)` → 结果落 `ScriptExecutionRecord` + 回写脚本失败计数/最近执行 → `TransmitAsync`（SignalR 回推执行事件）。
4. 防御：超时/递归限制/严格模式（沙箱层）；熔断阈值 3 次→`Tripped` 自动跳过；挂死看门狗 120s 强制熔断并放行租约。

### 5.5 定时任务数据流
1. `ScheduledTaskScheduler`（IHostedService）StartAsync：`ReloadAsync` 拉取任务、`ComputeNextUtc`（先按 6 段秒级 Cron，失败按 5 段分钟级，`ScheduleZone=Asia/Shanghai`），进入调度 Tick（1s），每 10 分钟兜底全量重载。
2. 循环到期（非重入）→ `DispatchFireAndTrack` → `DispatchAsync`：置 Running→按 `task.Type` 找注册的执行器（策略模式）→ `ExecuteAsync`（整体 30min 超时）→ 回写 Success/Failed/Skipped 到 `ScheduledTasks` 表。手动 `RunAsync` 走同一 `DispatchAsync`。
3. 执行器 4 种：
   - `SetValue`：写固定变量（调 `IRuntimeDeviceManager.WriteVariableAsync`）。
   - `ExecuteScript`：调 `IScriptEngineHost.RunAsync(scriptId)`。
   - `ClearHistory`：解析 `retentionDays` → `IInfluxStore.DeleteBeforeAsync(UtcNow-retentionDays)`。
   - `Backup`：MySQL 业务表(排除 SystemLogs/ConfigLogs/DbVersions/VariableHistories)导出 JSON + Influx 全量 CSV + manifest → 打包 `Backups/backup_时间戳.zip`。

---

## 6. 核心入口文件、关键类说明

| 文件 | 作用（关键点） |
|---|---|
| [`RuntimeManager.cs`](../ScadaServer.Runtime/RuntimeManager.cs) | 总控。`InitializeAsync` 加载设备；`RegisterDeviceAsync/RemoveDeviceAsync/ReloadDeviceAsync/ReconnectDeviceAsync` 动态管设备；`StartAsync/StopAsync`；`WriteVariableAsync` 统一写入口（限幅+超时+审计）；`StatusChanged` 事件 + `_lastPushedStatus` 去重。同时实现 `IRuntimeManager` + `IRuntimeDeviceManager`。 |
| `Devices/DeviceScheduler.cs` | 派发调度。每 50ms tick，单设备单 Worker，重连退避表 `_retryAfter`(5s)，优雅停止聚合等待 Worker。 |
| `Devices/DeviceWorker.cs` | 采集主循环。变量级 `NextPollTime` 调度；`ReconnectAfterConsecutiveFailures=3` 断线转重连；历史/实时/报警/通知逐项处理；平均响应时间累积移动平均。 |
| `Devices/DeviceRuntime.cs` | 设备运行时对象：持有 Device/Model/Protocol/Config/Area/Driver/Variables(`Dictionary<int,VariableRuntime>`)；`DispatchSync` 锁（派发与注销串行化）、`Lock`（采集锁）、`CreateWorkerCts/CancelWorker/DisposeWorkerTokenIfCurrent`（Worker 取消）。 |
| `Variables/VariableRuntime.cs` | 变量运行时：`Definition`(ModelVariable 模板)+`Instance`(DeviceVariable 实例)，解析出 `Address/BitOffset/PollingIntervalMs/Scale/DeadBand/IsReadOnly` 等；含历史写入状态与绑定回显窗口字段。 |
| `Events/VariableChangeBus.cs` | 进程内事件总线（Singleton），非阻塞、逐订阅者 try/catch。采集与写路径都 `Publish`。 |
| `Bindings/VariableBindingEngine.cs` | 绑定转发：加载环检测(自环+多节点 DFS)、有界 Channel(容量10000, DropOldest)、单消费者循环串行转发、回显抑制。 |
| `Alarms/AlarmRuleEngine.cs` | 报警规则热重载：构造时跑一次 + `Timer` 每 30s `ReloadSilentAsync` 拉快照，读侧引用替换，写成锁保护。 |
| `Scripting/ScriptEngineHost.cs` | 脚本引擎宿主：250ms Tick 调度 + 有界派发队列 + LongRunning 消费线程(默认2) + 熔断(3次) + 挂死看门狗(120s) + 执行记录落库 + SignalR 回推。 |
| `Scripting/ScriptRuntimeAccess.cs` | 脚本读写桥：按(DeviceKey,VariableKey)找运行时变量，`read/getQuality/write`；写桥同步阻塞但 `WriteBridgeTimeoutMs`(默认6000) 上界 + 孤儿任务观测。 |
| `Scripting/ScriptSandbox.cs` | Jint 沙箱：`LimitRecursion(100)` + `TimeoutInterval` + `Strict`；白名单 API：`log/read/getQuality/write`；`crossing read/write 授权`：读要设备在读授权串，写要"设备键.变量键"在写授权串(空=全拒)。 |
| `Tasks/ScheduledTaskScheduler.cs` | 任务调度：1s Tick，Cron 优先 6 段秒级否则 5 段，防重入 `_inflight`，10 分钟全量重载，状态回写 `ScheduledTasks` 表。 |
| `Tasks/BackupTaskExecutor.cs` 等 | 4 种任务执行器（备份/清历史/执行脚本/写变量），策略模式按任务类型派发。 |

**配置项（Runtime 读取，键名来自源码 `IConfiguration`）：**
- `Devices:WriteTimeoutMs`：设备驱动写兜底超时（默认 5000，收敛 500~60000）。
- `Scripting:Consumers`（默认2，1~16）、`Scripting:QueueCapacity`（默认256，8~10000）、`Scripting:WriteBridgeTimeoutMs`（默认6000，500~60000）。
- `ScheduledTasks:BackupOutputDir`：备份输出目录（默认 `Backups`，相对内容根目录）。
- `AlarmRuleEngine`/`ScheduledTaskScheduler` 中的调度时区硬编码为 `Asia/Shanghai`（脚本与任务 Cron 统一按北京时间）。

---

## 7. 外部依赖 & 第三方交互

| 对象 | 交互方式 | 超时 | 重连/容错 |
|---|---|---|---|
| 西门子 PLC（`S7Driver`） | `IProtocolDriverFactory` 按 `Protocol.DriverKey="S7"` 创建；`ConnectAsync/ReadAsync/WriteAsync/DisposeAsync`。驱动层自带建链/IO 超时（默认 `ConnectTimeoutMs`5000）。 | 驱动层封顶 + Runtime 层 `Devices:WriteTimeoutMs` 兜底。 | 采集连续 3 轮全失败 → `NeedsReconnect` → 调度器按 5s 退避触发 `ReconnectDeviceAsync` 重建驱动连。 |
| 其他驱动（OPC UA / 虚拟） | 同一工厂/接口。 | 由 Runtime 统一兜底写入超时。 | 同上统一重连机制（虚拟设备无真实连接）。 |
| ModbusTCP / MQTT 驱动 | 【缺少文件/未实现】创建即抛 `NotSupportedException`；MQTT 服务另有 `MqttHandler`(占位)。 | - | - |
| MySQL（EF Core） | 加载/持久化配置、报警记录、脚本执行记录、任务状态、实时快照 Upsert（经 `IServiceScopeFactory` 新 Scope）。 | 由 EF/连接串。 | 启动等待 DB 迁移就绪；脚本/告警/历史写入失败仅记日志不重试。 |
| InfluxDB 2.x（`IInfluxStore`） | 历史采样批量写、历史清理、备份全量导出。单例，配置变更热重建客户端。 | - | Influx 写失败回退写 MySQL（见 HistoryRecorder）。 |
| SignalR（`IScadaNotificationService` → `SignalRNotificationService`） | 变量更新/设备状态/报警/脚本执行事件实时推送。 | - | 推送失败仅告警，不阻塞采集（多为后台 fire-and-forget）。 |

---

## 8. 潜在问题与优化建议

> 以下均基于本次读到的源码逐条核对，标注严重级别。未在本次文件范围内的，标【缺少文件，无法确认】。

### 必须修复（阻断/高危）

1. **备份文件含敏感数据（安全）**
   `BackupTaskExecutor` 把 `SystemUsers`（含密码哈希字段）和 `DatabaseConfigs`（很可能含数据库连接命令/口令）以明文 JSON 打进 `Backups/*.zip`，存放在服务内容根目录，未加密、未设访问控制、未做保留清理。
   → 建议：备份时对敏感列脱敏或整体加密 zip；备份目录禁止 web 可访问；配置保留期并定期清理。

2. **无超时/无界任务的挂起风险（可靠性，脚本侧）**
   脚本专用消费线程若被一条真挂死脚本占用，.NET 无法 `Thread.Abort`，该线程成为泄漏。默认仅 2 个消费线程，若 2 条脚本都挂死 → 后续脚本派发全部只能入队或丢弃，全局脚本停摆。看门狗只能"标记熔断 + 放行租约"，回收不了线程。
   → 建议：提高 `Scripting:Consumers`、给高频 on-change 脚本加执行频率上限、上线前压测脚本健壮性；作为已知限制在运维文档中明确。

3. **历史/实时写入失败即丢数据（数据完整性）**
   `HistoryRecorder`：Influx 失败回退 MySQL，而 MySQL 写失败/队满(容量20000 DropWrite)直接丢弃且不重试。实时快照、绑定转发(容量10000 DropOldest)同理，突发或 DB 故障期间数据静默丢失。
   → 建议：至少对该类丢弃加更显眼的告警/持久化队列/落盘重补，避免现场"趋势断档、联动丢值"难排查。

### 可选优化（改进/规范）

1. **线性查找过多**：`WriteVariableAsync`、`VariableBindingEngine`、`ScriptRuntimeAccess` 里都用 `Variables.Values.FirstOrDefault(v=>v.Key==varKey)` 的 O(n) 找变量；设备变量多时每次写都全扫。
   → 建议：给 `DeviceRuntime` 增加按 `VariableKey` 的索引字典（`Dictionary<string, VariableRuntime>`）或用内存别名索引。

2. **`RealtimeSnapshotService` 的 Upsert 是"查一批 + 逐个 FindAsync"**：更新路径对每行 `FindAsync`，设备/变量数量大时每 1s 一次 N+1 查询。
   → 建议：批量主键装箱一次查询后映射更新；或改用 Upsert 原生 SQL。

3. **重复的数值化逻辑**：`RuntimeManager`（`TryToNumber/ToNumericSnapshotValue`）、`DeviceWorker`（`TryToNumber/IsEffectiveChange`）、`ScriptEngineHost` 里的浮点转换几乎相同的代码重复三处。
   → 建议：抽一个共享 `ValueConverter` 工具类。

4. **`AlarmRuleEngine` 构造即 fire-and-forget `_ = ReloadSilentAsync()` + `Timer` 回调 `GetAwaiter().GetResult()`**：构造期异步 + 定时器内同步阻塞等待，逻辑上没问题但可读性差；数据库未就绪时构造期首轮加载必然失败被静默。
   → 建议：改为事件驱动重载或显式由 Runtime 初始化的时机调用 `ReloadAsync`，减少对"30s 后自愈"的依赖。

5. **调度器重连退避固定 5s**：设备长期宕机时会每 5s 触发一次完整重连（含 TCP 建链），对网络/日志有一定压力。
   → 建议：指数退避（如 5s→10s→...→上限），减少无效尝试。

6. **脚本 Cron / 任务 Cron 时区硬编码 `Asia/Shanghai`**：跨时区部署/运维期待本地时间时可能困惑。
   → 建议：改为配置项，但需与前端展示统一约定。

7. **同一写入来源重复可达入入口，缺少幂等 UID**：绑定转发与脚本写入共用 `WriteVariableAsync`，对"值相同"的重复写会照样走驱动（除绑定有回显抑制外），严格场景下存在重复写审计噪音。
   → 建议（可选）：写入口增加"若值未变则跳过"的幂等短路（需谨慎，避免漏写）。

【缺少文件，无法确认】项：S7Driver/OpcUaDriver 具体超时与重连细节、`SignalRNotificationService`/`AlarmRecorder`/`VariableWriteAuditRecorder` 实现细节、以及脚本代码粒度与授权串如何配置，均在 Runtime 范围之外，需另补相关文件后再评估。

---

## 9. 新手阅读顺序建议

想最快读懂 Runtime，建议按以下顺序（从"外部如何触发"到"内部如何跑"）：

1. **`RuntimeManager.cs`** —— 项目"总地图"，先看它怎么加载设备、怎么对外暴露写入口、`StartAsync`/`StopAsync`。搞懂它，全局就有骨架。
2. **`Devices/DeviceRuntime.cs` + `Variables/VariableRuntime.cs`** —— 数据模型的地基：一台设备运行时"身上有什么"、一个变量"地址哪来的、值存哪"。这是理解所有采集/写流程的前提。
3. **`Devices/DeviceScheduler.cs` + `Devices/DeviceWorker.cs`** —— 采集心脏。看 Worker 的 `WorkerAsync` 主循环里那几行 `Driver.ReadAsync`、`_changeBus.Publish`、`TryRecordHistory`、`TryCheckAlarm`，就明白"一条实时数据怎么从 PLC 到前端/数据库"。
4. **`Events/ + Bindings/`** —— 数据怎么"动起来"。先看事件总线（谁 publish 谁 subscribe），再看绑定引擎如何基于总线做联动。
5. **`Alarms/AlarmRuleEngine.cs` + `DeviceWorker.TryCheckAlarm`** —— 报警怎么触发。
6. **`Scripting/ScriptEngineHost.cs` + `ScriptSandbox.cs`** —— 系统脚本自动化：懂了它就知道"复杂联动/自动逻辑"落点，也是全项目并发设计最讲究的地方。
7. **`Tasks/ScheduledTaskScheduler.cs` + 4 个 Executor** —— 运维自动化：备份/清历史等，相对独立，可放最后。
8. 最后对照看 **`WebApi/HostedServices/RuntimeHostedService.cs`** 和 **`WebApi/Extensions/Infrastructure.Extensions.cs`**，把"宿主怎么拉起 Runtime、DI 怎么注册"串起来——这就是完整的闭环。

---

## 10. 后续可扩展方向（可选）

1. **驱动扩展**：`ProtocolDriverFactory` 已留出"登记新驱动分支即可"的扩展点；补 `ModbusTCP`、`MQTT` 驱动时无需改 Runtime 采集/写逻辑（驱动只按 `IRuntimeDevice/IRuntimeVariable` 工作）。
2. **监控/健康检查**：`VariableBindingEngine.GetStats()`、脚本派发观测日志、设备 `SuccessCount/FailureCount/AverageResponseTime` 都已是就绪的指标源，可接入 `/metrics` 或运维大盘。
3. **按变量索引优化**：给变量建 Key 索引字典，支撑更大规模设备/变量。
4. **写入阈值/幂等增强**：写入口加"值未变则短路"的能力，减少无效驱动 I/O 与审计噪音。
5. **重连指数退避**：把 5s 固定退避升级为指数退避，降低设备大面积宕机时的重连风暴。
6. **备份安全管理**：脱敏/加密/保留期清理，满足企业交付，详见第 8 节第 1 条。

---

### 文档版本维护（预留接口）
本文档为 v1.0（初版）。后续如需更新，请提供**新增/变更文件及对应说明**，我会按其增量补齐对应章节（尤其是第 3、6、7、8 节），保持版本连贯（当归档 v1.x 的 x 递增）。如提供文件涉及 Runtime 外部（驱动、SignalR、记录器实现、脚本配置等），将一并纳入并更新"缺少文件"标注。