# Runtime 改造方案：需要重构项与保持现状项

> 依据：`docs/Runtime重构想法.md`（下称「方案」）+ 对现状 `ScadaServer.Runtime` 的逐项差异分析
> 状态：**草案，待用户审批，未改动任何代码**
> 日期：2026-09-03
> 关联分支：feature/db-refactor（仅记录方案，不在本文件范围内动代码）

---

## 一、TL;DR（结论先行）

| 类别 | 项 | 结论 |
|---|---|---|
| **需要重构** | R1 设备运行状态建模（RunState / HasAlarm / StateChangedAt） | ✅ 做，价值最高，现状完全空白 |
| **需要重构** | R2 通信状态补全（LastError / ReconnectCount / 状态变更时间） | ✅ 做（最小档），补齐缺失字段并打点 |
| **需要重构** | R3 聚合快照 DTO + `GET /api/devices/{id}/runtime` | ✅ 做（后端 + 可选前端），吸收方案 §十六 |
| **需要重构** | R4 值容器 RuntimeValueStore 化 | ⏸️ 暂缓：收益低、牵动采集/写入全链路，本阶段不做 |
| **不重构** | N1 RuntimeConfig POCO 解耦 EF 实体 | ❌ 不做（方案 §十五 教条，与热注册机制冲突） |
| **不重构** | N2 RuntimeState 生命周期状态机 | ❌ 不做（IsRunning/WorkerTask/NeedsReconnect 已等价覆盖） |
| **不重构** | N3 枚举改名与语义对齐（ConnectionState/DataQuality） | ❌ 不做（现状枚举更细且全库引用） |
| **不重构** | N4 值身份 long DataPointId 全局化 | ❌ 不做（与多模型模板体系冲突） |
| **不重构** | N5 驱动批量读改写采集主循环 | ❌ 不改（现状逐变量调度更优；ReadBatchAsync 留作可选优化） |
| **不重构** | N6 ScanInterval → PollingIntervalMs | ✅ 已等价落地，无需再动 |
| **顺带清理** | C1 删除死代码 `Domain/Entities/DeviceRuntime.cs` | ⚠️ 建议做（同名易混淆，全库无引用） |

> 一句话：**改「建模缺口」，不改「工程实现」。**

---

## 二、判定标准（为什么这样切）

每项对照三个问题：

1. **收益**：是否解决前端/运维真实可见的缺口（状态语义缺失、聚合视图缺失）？
2. **成本**：改动是否波及采集主循环、锁语义、EF 热注册/重连等关键路径？
3. **兼容度**：是否与现状「DB 管配置、Runtime 管状态」及阶段 5/6 已落地的 Connection 单真相源、DeviceDataModel 多对多一致？

判定结果自然分成两拨：

- 方案**教条**（POCO 解耦、生命周期状态机）是为"从零起步"设计的，对已经跑通的现役系统是**负资产** → 不重构；
- 方案**语义缺口**（机器状态、LastError、快照）是现状真没有的能力，且可以**不动采集与锁语义**增量补齐 → 重构。

---

## 三、需要重构的项（R1 – R3）

### R1 设备运行状态建模（RunState / HasAlarm / StateChangedAt）

**方案引用**：§四「设备状态 ≠ PLC通信状态」、§五 `DeviceStatusRuntime`、§十二 Status 区。

**现状**（缺口）：
- `DeviceConnectionState`（连接态）→ 经 `RuntimeManager.MapConnectionStateToStatus`（RuntimeManager.cs:745–758）映射为对外 `DeviceStatus`（Offline/Online/Fault/ConfigUpdating/Connecting）。
- **没有**设备级"运行/停止/暂停/维护"语义；**没有**设备级 `HasAlarm` 聚合；**没有**状态变更时间戳。
- 前端要判断"机器在不在跑"只能读某个约定变量自行推断，Runtime 层不提供该语义。

**Before（现状模型）**：

```
DeviceConnectionState (连接态) ──映射──▶ DeviceStatus (对外 5 态)
   [无 RunState] [无 HasAlarm] [无 StateChangedAt]
```

**After（目标模型）**——新增两个正交维度，连接态映射保持不变（避免破坏现有推送/落库语义）：

```
DeviceRuntime
 ├── ConnectionState   （既有，连接态 → DeviceStatus 映射不变）
 └── DeviceRunState    （新增，机器状态：Unknown/Stopped/Running/Paused/Fault/Maintenance）
     HasAlarm          （新增，设备级报警聚合）
     StateChangedAt    （新增，RunState 最近变更时刻，UTC）
```

**设计要点**：
1. 新增 `DeviceRunState` 枚举（照方案六态：Unknown/Stopped/Running/Paused/Fault/Maintenance），放 `ScadaServer.Domain/Enums/`。
2. 在 `ScadaServer.Runtime/Devices/DeviceRuntime.cs` 上新增三个字段 + `SetRunState()` 收口方法（内部仅当值变化时更新并推进 `StateChangedAt`，与方案 `DeviceStatusRuntime.SetRunState` 语义一致，但不引入子对象分区，避免大改）。
3. **HasAlarm 数据来源（决策点 D1）**：
   - 设备级报警聚合来自 `DeviceWorker` 的规则报警/上下限报警状态（`_ruleStates`/`_alarmStates`，DeviceWorker.cs:42–47）。
   - 推荐最小侵入：在 `DeviceRuntime` 上暴露 `MarkAlarm(bool)` / `ClearAlarm()`（或 `SetAlarmState(bool)`），由 `DeviceWorker` 在报警触发/恢复的既有出口 `FireEvent`（DeviceWorker.cs:735）与 `CheckMinMaxLimit`（L689）处调用——这两处已天然区分 Triggered/Recovered。
4. **RunState 数据来源（决策点 D2）**——这是本项唯一需要用户拍板的设计：
   - **选项 A（手动/API）**：先建模型，RunState 由管理员经设备操作接口显式置位（适用：不依赖 PLC 变量、需要人工干预停机/检修状态的场景）。
   - **选项 B（约定变量自动推导，推荐最终态）**：在模型变量层约定一个"运行状态变量"（如某 Bool 变量 = 运行中），由运行时推导。但现状模型变量无该标记字段，引入需动 DataPoint/DataPointMapping 定义 → 成本上升。
   - **选项 C（分两步：先 A 后 B）**：本阶段先落地模型 + 手动置位 + 快照输出；自动推导作为后续阶段（需在设计层加"运行状态变量"标记字段时再一并做）。**推荐 C。**
5. RunState 变化时是否推送：复用现有 `RuntimeManager.StatusChanged` 通道会改变 DeviceStatus 语义，**不建议**；改为 RunState 作为快照字段供轮询/前端主动刷新，实时推送留到后续统一做（避免本次动 SignalR 消息协议）。

**改动文件清单**：
| 文件 | 改动 |
|---|---|
| `ScadaServer.Domain/Enums/DeviceRunState.cs` | 新增枚举 |
| `ScadaServer.Runtime/Devices/DeviceRuntime.cs` | 新增 `RunState`/`HasAlarm`/`StateChangedAt` + `SetRunState()`/`SetAlarmState()` |
| `ScadaServer.Runtime/Devices/DeviceWorker.cs` | 报警触发/恢复出口调用 `SetAlarmState`（约 L735 / L689 两处） |
| 控制器/接口 | 若选 D2-A 需加置位端点（见 R3 一并设计，避免重复建控制器） |

**验收**：单测或冒烟——设备 A 置 Running 后 `HasAlarm` 随报警触发翻转、`StateChangedAt` 仅在状态变化时推进；连接态映射 DeviceStatus 行为与改造前完全一致（回归比对）。

---

### R2 通信状态补全：LastError / ReconnectCount / 状态变更时间

**方案引用**：§四 `ConnectionRuntime`（State / LastSuccessTime / LastErrorTime / LastError / ConsecutiveFailures / ReconnectCount / ReadCount / ErrorCount + MarkSuccess/MarkError/MarkReconnecting）。

**现状**：
- 计数/时间字段已在 `DeviceRuntime` 平铺（LastCommunicationTime L92、LastPollTime L95、ConsecutiveFailureCount L98、SuccessCount L101、FailureCount L104、PollRoundCount L107、AverageResponseTime L110），由 `DeviceWorker` 手动维护（成功分支 L273–279、失败分支 L280–307）。
- **缺失**：`LastError`（最近一次失败原因）、`ReconnectCount`（自动重连累计次数）、连接态变更时间。
- 重连语义现状由 `NeedsReconnect` + `DeviceScheduler` 退避窗口表达（DeviceScheduler.cs:61,131–140,271），机制上比方案更完备，缺的只是**计数与原因**两个可观测字段。

**改造（推荐最小档，不动现有字段归属与采集逻辑）**：
1. `DeviceRuntime` 新增：
   - `public string? LastError { get; private set; }`
   - `public int ReconnectCount { get; private set; }`
   - `public DateTime? ConnectionStateChangedAt { get; private set; }`
2. `ConnectionState` 属性 setter（DeviceRuntime.cs:57–67）内补充推进 `ConnectionStateChangedAt = DateTime.UtcNow`（仅值变化分支内）。
3. 打点位置：
   - 失败原因：`DeviceWorker` 失败分支 catch（L244–253 变量读失败 / L309–316 整轮失败）写入 `LastError`（取 `ex.Message`，截断如 500 字符）；Scheduler 重连触发处可不清空。
   - 成功清零：成功分支（L273–279）将 `LastError` 置 null（可选，决策点 D3）。
   - 重连计数：`RuntimeManager.ReconnectDeviceAsync`（L261–282）通过重入门闸后 `runtime.ReconnectCount++`；`BuildAndRegisterDeviceAsync` 连接失败路径（L439–450）也应 +1（这是初始连接失败也会重试的场景，计数口径需用户定：**初始失败算不算一次 Reconnect**）。
4. 完整分区档（把上述字段收进独立 `ConnectionRuntime` 子对象）**本阶段不做**——所有引用方（Worker/Scheduler/RuntimeManager/状态推送）都直读平铺字段，拆对象属纯结构调整、风险高收益低。留给将来「统计/连接语义大规模扩展」时再议。

**改动文件清单**：
| 文件 | 改动 |
|---|---|
| `ScadaServer.Runtime/Devices/DeviceRuntime.cs` | 3 个新字段 + setter 推进时间戳 |
| `ScadaServer.Runtime/Devices/DeviceWorker.cs` | 失败 catch 写 `LastError`（L244–253、L309–316），成功分支可选清零 |
| `ScadaServer.Runtime/RuntimeManager.cs` | ReconnectDeviceAsync（L261+）与连接失败占位（L439–450）递增 `ReconnectCount` |

**验收**：模拟 S7 连不通 → 快照中能看到 `LastError` 非空、`ReconnectCount` 递增、`ConnectionStateChangedAt` 随 Error/Connected 翻转变化。

---

### R3 聚合快照 DTO + GET /api/devices/{id}/runtime

**方案引用**：§十六 `DeviceRuntimeSnapshot` + `GET /api/devices/{id}/runtime`，前端拉一次拿到连接态/运行态/报警/值概要，实时变化仍走 SignalR（避免轮询）。

**现状**：
- 状态出口只有 `IRuntimeStatusProvider.TryGetRuntimeStatus(int, out DeviceStatus)`（Application/Interfaces/IRuntimeStatusProvider.cs:19，适配 RuntimeStatusProviderAdapter.cs:20，最终 RuntimeManager.cs:555–563）——**单一 5 态枚举**，无聚合视图。
- 值出口是变量级 SignalR 推送（NotifyVariableUpdateAsync）+ 每变量 MySQL `VariableRealtime` 表（IRealtimeSnapshotService）。

**After**：新增快照 DTO，一次返回设备身份 + 双状态（连接态 + 运行态）+ 报警 + 关键统计 + 值概要（可选），供前端进入设备详情页时初始化一次。

**设计要点**：
1. 新增 DTO `DeviceRuntimeSnapshotDto`（放 `ScadaServer.Application/DTOs/`）：
   ```
   DeviceId / Key / Name
   DeviceStatus（对外 5 态，向后兼容）
   ConnectionState / RunState / HasAlarm
   LastSuccessTime(LastCommunicationTime) / LastError / ConsecutiveFailureCount
   ReconnectCount / AverageResponseTime / ValueCount
   Values?: IReadOnlyList<RuntimeVariableSnapshotDto>   // 决策点 D4
   ```
2. 组装：新增 `DeviceRuntimeSnapshotBuilder`（Runtime 层），从 `DeviceRuntimes` 字典取运行时对象组装；未注册（禁用/初始化失败）返回 null → 端点 404 或回退默认（决策点 D5）。
3. 应用层接口扩展：`IRuntimeStatusProvider` 增加 `bool TryGetRuntimeSnapshot(int deviceId, out DeviceRuntimeSnapshotDto? snapshot)`（新增 DTO 到 Application 层，避免 Application 反向依赖 Runtime——沿用现状 Adapter 分层）。
4. Controller 端点：`GET /api/devices/{id}/runtime`（放在现有 DeviceController 或新增 RuntimeController，写路径不涉及 → 仅认证即可）。
5. **Values 是否含在快照内（决策点 D4）**：
   - 选项 1（推荐，先不含）：快照只含状态与统计；值仍走既有 SignalR/实时表。原因：全量 values 快照与前端订阅模型重复，且大 JSON 有序列化成本；设备详情页首帧用状态 + 订阅实时值即可。
   - 选项 2（含 values，`?includeValues=true` 可选）：适合前端希望"一次拉全"的场景，但需约定上限（如 >500 变量截断）。
6. RunState 置位端点（若 D2 选 A/C）：在同一控制器提供 `PUT /api/devices/{id}/runtime/runstate`（RequireAdmin + AuditLog），与设备写操作惯例一致。

**改动文件清单**：
| 层 | 文件 | 改动 |
|---|---|---|
| Application | `DTOs/DeviceRuntimeSnapshotDto.cs`（+`RuntimeVariableSnapshotDto`） | 新增 |
| Application | `Interfaces/IRuntimeStatusProvider.cs` | 增 `TryGetRuntimeSnapshot` |
| Runtime | `RuntimeManager.cs` | 增快照组装方法（或新增 `Snapshot/DeviceRuntimeSnapshotBuilder.cs`） |
| WebApi | `Services/RuntimeStatusProviderAdapter.cs` | 转发新方法 |
| WebApi | `Controllers/…RuntimeController.cs`（或并入 DeviceController） | `GET …/runtime`（+可选 runstate PUT） |
| Client（可选） | 前端设备详情/监控页首帧消费快照 | 决策点 D6 |

**验收**：登录后 `GET /api/devices/{id}/runtime` 返回双状态与统计；设备连接失败 → ConnectionState=Error/DeviceStatus=Fault/LastError 非空；对比既有 `TryGetRuntimeStatus` 5 态结果一致（不回归）。

---

### R4 值容器 RuntimeValueStore 化 —— 判定：暂缓

**方案引用**：§六 `RuntimeValue`（DataPointId/Value/PreviousValue/Quality/Timestamp/SourceTimestamp/Changed/UpdateCount/Update()）、§八 `RuntimeValueStore`（ConcurrentDictionary + Get/Update/GetAll/取 Changed 批量）。

**为什么暂缓**（不是不做，是性价比与风险不支持现在做）：
- 现状 `VariableRuntime`（Variables/VariableRuntime.cs:94–107）已具备 Value/PreviousValue/UpdateTime/Quality/IsChanged 全部"值快照"语义，**且额外叠加**了调度游标（NextPollTime）、历史游标（LastHistoryTime/LastHistoryWrittenValue）、回声抑制字段（LastBindingWriteValue/Time）——信息量比方案的纯净 RuntimeValue 更大。
- 将值快照拆成独立 `RuntimeValue` + `RuntimeValueStore` 需要动：DeviceWorker 采集主循环（L161–259 直接读写 `vr.Value` 等）、写值链路、回声抑制、绑定引擎、历史/实时/报警判定——**本质是重写**，收益仅在"批量取 Changed 推送"一个场景。
- 现状用设备级 `SemaphoreSlim Lock`（DeviceRuntime.cs:113）+ 单线程采集保证一致性，方案用 ConcurrentDictionary 按变量并发——**两种都正确**，迁移无功能增益。

**将来触发条件**（满足其一再启动）：需要设备级批量 Changed 快照做 SignalR 批量推送优化；或值需要支持独立于采集的并发写者。届时以「在 VariableRuntime 上加批量快照读取方法」的轻量方式做，而非引入 RuntimeValue 类。

---

## 四、不重构的项（N1 – N6，保持现状）

### N1 RuntimeConfig POCO 解耦 EF 实体 —— 不做

- 方案 §十五反对 `DeviceRuntime { Device Device; ICollection<DataPointMapping> Mappings; }`，主张 EF Entity → RuntimeConfig POCO → DeviceRuntime。
- 现状确实直挂 EF 实体（`DeviceRuntime.Device/Model/Protocol/Area`，DeviceRuntime.cs:18–47；加载链 RuntimeManager.cs:177–185 Include Controller/Connection→Protocol/Model/Mapping→DataPoint）。
- **但不改**：① 热注册/重连/热更新依赖 EF 追踪与对象图重载（RegisterDeviceAsync/ReloadDeviceAsync L222–247），改成 POCO 需重写整个注册与重载链路；② 阶段 6 已把"驱动视角"收敛到只读接口 `IRuntimeDevice`/`IRuntimeVariable`，**驱动已不感知 EF 实体**——方案担心的耦合在驱动边界已被消除；③ 收益仅在"Runtime 可脱离 DbContext 单测"，而现状 RuntimeManager 本就按 scope 隔离使用 DbContext，无实际痛点。结论：**方案教条，代价高收益低，不做**。

### N2 RuntimeState 生命周期状态机 —— 不做

- 方案 §十一建议 Created→Starting→Running→Stopping→Stopped 五态。
- 现状等价覆盖且更细：`IsRunning` + `NeedsReconnect` + `WorkerTask`（派发/所有权收口）+ `CreateWorkerCts/CancelWorker/DisposeWorkerTokenIfCurrent`（单设备启停，DeviceRuntime.cs:76–162）+ `DispatchSync` 派发临界区（L89）+ Scheduler 热任务派发（DeviceScheduler.cs:104+）。
- 状态机化是"表现形式"而非"能力"，会为追状态引入额外状态同步成本。**不做**。

### N3 枚举改名与语义对齐 —— 不做

- 方案 `ConnectionState` 六态（含 Reconnecting/Faulted/Disabled）vs 现状 `DeviceConnectionState` 六态（Unknown/Connecting/Connected/Disconnected/Error/Initializing）；重连语义由 NeedsReconnect+退避表达而非新增枚举值。
- 方案 `DataQuality`（Good/Bad/Uncertain）vs 现状 `VariableQuality` 八态（含 CommunicationError/Timeout/NotConnected/DeviceOffline/Initializing）——**现状更细**，是僵尸值语义的支撑。
- 两套枚举均为 Domain 级、被协议驱动/历史/实时/前端大量引用，改名纯属对齐文档命名，零功能收益。**不做**。R2 补的字段直接挂在现有枚举语义上。

### N4 值身份 long DataPointId 全局化 —— 不做

- 方案以全局 `DataPointId(long)` 作为值身份；现状对外身份是 `(DeviceId, VariableKey 字符串)`，内部容器 key 是 `DataPointMapping.Id(int)`，同时支撑多数据模型模板体系与绑定/规则引擎（均按 Key 寻址）。
- 全局 long 身份与「同一模板变量被多台设备实例化」的多对多模型冲突，且 DataPoint 模板本身是全局概念、DataPointMapping 才是设备实例——方案混淆了这两层。**不做**。

### N5 驱动批量读改写采集主循环 —— 不改

- 方案 Demo 的 `Driver.ReadAsync() → IReadOnlyList<DriverValue>` 是"整轮批量读、统一 500/1000ms 周期"；现状 `DeviceWorker` 按 `NextPollTime` **逐变量独立调度**（DeviceWorker.cs:110–143），比方案粒度更细，正是方案 §十三自己想要的"不要让 Worker 决定周期"。
- `IProtocolDriver.ReadBatchAsync` 接口虽已定义但采集主循环仍逐变量 `ReadAsync`（DeviceWorker.cs:166）——这是**可选优化**（高点数设备的批量读优化），不是重构；且 S7/OPC UA 是否支持批量读取决于驱动实现。**本轮不动**，作为独立性能优化候选登记。

### N6 ScanInterval → PollingIntervalMs —— 已等价落地

- 方案 §十三的"DataPointMapping.ScanInterval 驱动调度"已实现：Mapping 级 `PollingIntervalMs`（VariableRuntime.cs:56），模板兜底，`NextPollTime` 调度（DeviceWorker.cs:115）。**无需再动**。

---

## 五、顺带清理项

### C1 删除死代码 `ScadaServer.Domain/Entities/DeviceRuntime.cs`

- 该实体（DeviceId/DeviceName/CurrentStatus/LastHeartbeat/ReconnectCount/LastError/UptimeSeconds…）**全库无任何引用**（Grep 已确认：无 DbSet、无 new、无 repo、无 controller 使用），是早期遗留的"内存状态实体"构想，与 `Runtime/Devices/DeviceRuntime.cs` 同名，极易造成误引用与认知混淆。
- 风险：极低（零引用，纯删除）。建议随本次改造作为独立小提交清理。**是否执行请在审批时确认（决策点 D7）。**

---

## 六、实施路线图（审批通过后执行）

> 遵循项目约定：先方案审批 → 增量代码 → 用户 review → Structured Conventional Commits 分组提交，默认不 push；改动范围严格限定在下表，不扩展到其他任务。

| 批次 | 内容 | 涉及层 | 验证 | 提交建议 |
|---|---|---|---|---|
| **B1** | R1 建模（枚举+字段+SetRunState/SetAlarmState+Worker 报警聚合）+ R2 补字段与打点 | Domain / Runtime | `dotnet build` 0 错误；冒烟：报警触发 HasAlarm 翻转、LastError/ReconnectCount 生效；连接态映射回归 | SCC 1–2 笔（后端） |
| **B2** | R3 快照 DTO + 接口 + Adapter + Controller + 冒烟 | Application / Runtime / WebApi | API 冒烟 `GET /api/devices/{id}/runtime`；与旧 5 态接口一致 | SCC 1–2 笔（后端） |
| **B3** | C1 删死代码（独立小提交，可与 B1 合并评审但分开 commit） | Domain | build 全绿 | SCC 1 笔 |
| **B4（可选）** | D2-A 的 runstate 置位端点、D6 前端消费快照 | WebApi / Client | API + 前端冒烟 | 按用户确认 |
| **暂缓** | R4 值容器化、D2-B 自动推导、ReadBatchAsync 批量读优化 | — | — | 另开会话立项 |

---

## 七、风险与回归面

| 风险 | 等级 | 缓解 |
|---|---|---|
| R1 HasAlarm 打点在 Worker 报警出口（L689/L735）引入新调用，若抛异常影响采集 | 低 | 打点调用用 try/catch 包裹（与 FireEvent 自身模式一致，L737/770） |
| R2 在 ConnectionState setter 里推进时间戳，setter 已被多方调用 | 极低 | 仅值变化分支内一行赋值，不改现有事件通知逻辑 |
| R3 新端点返回大 values 导致序列化压力 | 中 | 默认不含 values（D4 选项 1）；含值时设上限截断 |
| 状态推送/DeviceStatus 语义被 R1 波及 | 低 | R1 明确**不**复用 StatusChanged/DeviceStatus，RunState 独立成字段 |
| 删死代码误伤 | 极低 | 已全库 grep 零引用；删除后 build 验证 |

**回归门禁（提交前必跑）**：`dotnet build`（slnx）0 错误；R1/R2 改动后对既有 S7 断线→重连→恢复路径做一次冒烟，确认 ConnectionState/DeviceStatus 时序与改造前一致。

---

## 八、决策点汇总（请审批时逐项给出选择）

| # | 决策点 | 选项 | 我的建议 |
|---|---|---|---|
| D1 | HasAlarm 由谁驱动 | a) DeviceWorker 报警出口打点聚合；b) 仅保留字段等后续报警中心统一驱动 | **a** |
| D2 | RunState 数据来源 | A) 手动/API 置位；B) 约定变量自动推导（需模型层加标记字段）；C) 先 A 后 B 分两步 | **C** |
| D3 | 采集成功后是否清空 LastError | a) 成功即清空；b) 保留最近一次错误直至下次失败覆盖（利于排障） | **b**（或 a，取决于排障习惯） |
| D4 | 快照是否含 Values | 1) 只含状态/统计（推荐）；2) 支持 `?includeValues=true` 可选 | **1** |
| D5 | 设备未注册时快照端点返回 | a) 404；b) 返回默认（Offline + 空值） | **a** |
| D6 | 前端是否本轮消费快照（设备详情首帧） | a) 本轮做；b) 后端先行，前端后续单独排 | **b**（按现状增量提交节奏） |
| D7 | 是否删除死代码 `Domain/Entities/DeviceRuntime.cs` | a) 删除；b) 保留 | **a** |

---

## 九、附录：现状关键行号速查（本文引用基线）

| 位置 | 说明 |
|---|---|
| `Runtime/Devices/DeviceRuntime.cs:57–67` | ConnectionState setter（事件通知） |
| `Runtime/Devices/DeviceRuntime.cs:76–162` | IsRunning/NeedsReconnect/WorkerTask/CreateWorkerCts/CancelWorker/DisposeWorkerTokenIfCurrent |
| `Runtime/Devices/DeviceWorker.cs:110–143` | 变量级 NextPollTime 调度 |
| `Runtime/Devices/DeviceWorker.cs:161–259` | 逐变量采集主循环（值更新/回声抑制/通知/历史/实时/报警） |
| `Runtime/Devices/DeviceWorker.cs:273–307` | 轮次级成功/失败判定、NeedsReconnect 置位 |
| `Runtime/Devices/DeviceWorker.cs:689–729` | Min/Max 兜底报警（Triggered/Recovered） |
| `Runtime/Devices/DeviceWorker.cs:735–774` | FireEvent（规则报警触发/恢复出口） |
| `Runtime/Devices/DeviceScheduler.cs:61,131–140,271` | 重连退避窗口与 NeedsReconnect 派发 |
| `Runtime/RuntimeManager.cs:177–185` | EF Include 加载链（Controller/Connection→Protocol/Model/Mapping→DataPoint/DeviceDataModels） |
| `Runtime/RuntimeManager.cs:381–495` | BuildAndRegisterDeviceAsync（连接→变量装配→注册） |
| `Runtime/RuntimeManager.cs:555–563` | TryGetRuntimeStatus（仅 5 态枚举） |
| `Runtime/RuntimeManager.cs:745–758` | MapConnectionStateToStatus |
| `Application/Interfaces/IRuntimeStatusProvider.cs:19` | 状态提供器接口（唯一状态出口） |
| `WebApi/Services/RuntimeStatusProviderAdapter.cs:20` | 适配实现 |
| `Domain/Entities/DeviceRuntime.cs:1–69` | **死代码实体（建议删）** |
