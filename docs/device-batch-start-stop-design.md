# 区域级设备批量启停 — 设计方案

> 状态：**已定稿（决策点 0-6 全部确认，档位 B + IsEnabled），待实施**（本文档只做设计，未改动任何代码）
> 日期：2026-09-21
> 版本：v2（已并入复核结论，见文末「修订记录」）
> 范围：整区域（含子孙区域）设备的批量启用/停用采集

---

## 0. 方案分档（决策点 0，先选投入档位）

先给两条路径供选择，改动成本与收益不同：

| 档位 | 做法 | 改动范围 | 解决的问题 | 遗留问题 |
|---|---|---|---|---|
| **A 纯前端（最小改动）** | 只改 `batchEnable/batchDisable`：for 循环改为**逐台独立 try/catch + `Promise.allSettled` 聚合**，补结果汇总弹窗 + 二次确认 | 仅 `DeviceManagementView.vue`，零后端改动 | #1 中断整批、#2 无部分失败、#5 无二次确认 | #3 仍 300+ 次 DB 查询、#4 无预检、#6 审计噪音、#7 状态推送风暴 |
| **B 后端批量 + 预检（完整，本文档主体）** | 后端新增批量接口 + 预检，前端只发一次请求 | 后端 3 处 + 前端 4 处 | 上述**全部七条** | 改动面更大，需一次迁移/联调 |

- 若只求快速止痛、区域设备量不大 → **选 A**，后续需要再升级到 B。
- 若要一步到位、且认可"预检 + 部分成功 + 单次请求"的体验价值 → **选 B**。

> 下文第 2~6 节按 **B** 展开；A 只需第 2.1 问题表 + 一个前端改造点，无需后端内容。

---

## 1. 术语澄清（决策点 1，请先确认）

项目里"启停"有两层完全不同的语义，必须先对齐，否则方案会跑偏：

| 语义 | 字段 | 当前机制 | 是否有接口 |
|---|---|---|---|
| **采集使能** | `Device.IsEnabled` | 启用→`RuntimeManager` 注册设备、建 Worker/驱动；停用→注销、断开、推 Offline | ✅ `PUT /api/devices/{id}/enabled` |
| 运行态 | `Device.RunState` | Unknown / Stopped / Running / Paused / Fault / Maintenance | ❌ 全库仅一处**读取**映射（`Runtime/Devices/RuntimeManager.cs:1037` 附近由运行时状态派生），无对外写入口 |

**本文档默认按"采集使能"（`IsEnabled`）设计** —— 对应界面上的"批量采集 / 批量停采"，也是唯一已存在启停接口的一层。

> 若你要的是"让设备进入 Running/Stopped 运行态"，那是另一套设计：需要驱动层支持运行态控制 + 运行态命令下发 + 状态回读，与采集使能无关。请明确。

---

## 2. 现状盘点（代码实证）

| 能力 | 现状 | 位置 |
|---|---|---|
| 区域树 | 已存在，`ParentId` 自引用树；`AreaTypeEnum` = Factory / Workshop / ProductionLine / Area / Warehouse | `Domain/Entities/Area.cs:28,56`、`Domain/Enums/AreaTypeEnum.cs` |
| 设备归属区域 | `Device.AreaId` + `Area` 导航 | `Domain/Entities/Device.cs:34,39` |
| 区域→设备集合 | **已有**，返回含子孙区域的设备 ID 列表 | `WebApi/Controllers/AreaController.cs:27` → `Application/Services/AreaAppService.cs:203` `GetDeviceIdsInSubtreeAsync` |
| 单设备启停 | **已有**，幂等 + 启动前地址校验闸门 | `WebApi/Controllers/DeviceController.cs:111` → `Application/Services/DeviceAppService.cs:885` `SetEnabledAsync` |
| 运行时归口 | 单例 `RuntimeManager`，内存态 `ConcurrentDictionary DeviceRuntimes` | `Runtime/RuntimeManager.cs:199/258/576` |
| 状态广播 | SignalR `/hubs/scada`，`ReceiveDeviceStatus(deviceId, status)` → `Clients.All` | `WebApi/Hubs/ScadaHub.cs`、`WebApi/Services/SignalRNotificationService.cs:89` |
| 前端区域筛选 | **已实现**：`selectedAreaId` + `includeSubareas` + `subtreeDeviceIds`（已调 `device-ids` 接口） | `Client/src/components/DeviceManagementView.vue:106-108,194-199,207-213` |
| 前端批量 UI | **已实现**：多选 checkbox + 批量工具栏（批量采集/停采/删除） | `DeviceManagementView.vue:111,241-292,648-686` |
| **后端批量接口** | **不存在** | — |

### 2.1 现有批量实现的方式与问题

现有 `batchEnable` / `batchDisable` 是**前端 for 循环逐台调单设备接口**：

```ts
// DeviceManagementView.vue:264-281
const batchEnable = async () => {
  const ids = Array.from(selectedDeviceIds.value);
  for (const id of ids) {
    await setDeviceEnabledAndSync(id, true);   // ← N 次 HTTP 往返
  }
  addLog('设备管理', `批量启用了 ${ids.length} 台设备的采集`, 'normal');
  clearSelection();
};
```

| # | 问题 | 严重度 | 说明 |
|---|---|---|---|
| 1 | **中途失败会中断整批** | 高 | 循环内任一台抛 `BusinessException`（如变量地址未配置），后续设备不再执行，且 `clearSelection()` 被跳过 → 选中态与真实状态不一致，用户无从判断"做到哪了" |
| 2 | **无部分失败语义** | 高 | 没有"成功 X / 失败 Y / 原因"的汇总，用户只能靠设备列表逐台看 |
| 3 | **N+1 放大到 N×3** | 高 | `SetEnabledAsync` 内部每台执行：1 次 `GetByIdForUpdateAsync`（`DeviceAppService.cs:887`）+ 校验查询 + **2 次** `GetByIdAsync` 返回完整 DTO（`:896`、`:921`）。100 台 ≈ 300+ 次 DB 查询 + 100 次 HTTP |
| 4 | **无预检** | 中 | 地址未配置的设备要等执行时才报错；理想是点击前就告诉用户"这 3 台启不起来" |
| 5 | **批量启用无二次确认** | 中 | `batchDisable` 有 `confirm`（`:274`），`batchEnable` 没有（`:264`），不对称 |
| 6 | **审计噪音** | 低 | 单设备接口每条触发一次 `AuditLog("SET_ENABLED")`，批量 100 台 = 100 条审计 |
| 7 | **状态推送风暴** | 低 | 每台注册/注销各推一次 SignalR 状态，批量时列表频繁闪动；且 `RegisterDeviceAsync` 内部**先 `RemoveDeviceAsync` 再重建**（`RuntimeManager.cs:264`），每台都是完整拆除+重建 |
| 8 | **启用/停用反馈节奏不对称** | 低 | 停用即时 Offline；启用要等连接建立（数秒，失败走占位重连）。若进度条按"请求返回"算 100%，会与设备真实在线状态脱节 |

---

## 3. 方案设计（档位 B）

核心思路：**把"批量"下沉为后端一等能力，前端只发一次请求；执行串行、逐台隔离、部分成功。**

### 3.1 API 契约

两个端点，共用同一请求体（`DeviceController` 内）：

```
POST /api/devices/batch/enabled            # 执行
POST /api/devices/batch/enabled/precheck   # 预检（只校验不执行）
```

**请求体**（二选一定位目标，同时传时以 `deviceIds` 为准）：

```jsonc
{
  "areaId": 12,               // 按区域：服务端自行解析子树
  "includeSubAreas": true,    // 是否含子孙区域
  "deviceIds": null,          // 或按显式 ID（复用表格跨区域多选）
  "enabled": true,            // true=启用采集, false=停用
  "skipInvalid": true         // 预检不通过的设备是否跳过（true 则不中断整批）
}
```

> **`precheck` 与 `skipInvalid` 只对 `enabled=true` 有意义**：停用不涉及地址校验，预检对停用返回空阻塞清单，`skipInvalid` 对停用不生效。

**响应**（部分成功模型）：

```jsonc
{
  "operationId": "b7f3...",        // 便于日志关联 / 前端聚合
  "total": 24,
  "succeeded": 21,
  "skipped": 1,
  "failed": 2,
  "items": [
    { "deviceId": 9, "name": "2#注塑机", "result": "Skipped", "reason": "已是启用状态" },
    { "deviceId": 7, "name": "1#注塑机", "result": "Failed",
      "reason": "有 3 个已启用变量的采集地址未配置：温度、压力、转速" }
  ]
}
```

> `skipped` 需在 `reason` 里区分两种来源：**已是目标状态**（幂等跳过）vs **预检阻塞且 `skipInvalid=true`**（按策略跳过），前端据此给不同文案。

**设计取舍**

- **为什么同时支持 `areaId` 和 `deviceIds`？**
  `areaId` 路径由服务端解析子树，避免前端算出的集合与后端实际区域不一致、也避免请求体膨胀；`deviceIds` 路径复用表格多选（可能跨区域）。两者归一化到同一个 `ResolveTargetsAsync(...) -> List<int>`，内部复用既有 `GetDeviceIdsInSubtreeAsync`。
- **权限**：与 `DeviceController` 保持一致，`[Authorize(Policy = "RequireAdmin")]` —— Operator 不应有启停权（符合既定角色隔离方案）。
- **审计**：目标只写一条，detail 带 `target / enabled / total / succeeded / failedDevices`。注意：现有 `[AuditLog("设备管理", "SET_ENABLED")]` 是**固定 action 的特性标注**，无法塞动态失败清单 → 需扩展该特性支持动态 detail，或在 service 内手写一条审计记录。

### 3.2 执行策略（关键决策）

| 决策 | 选择 | 理由 |
|---|---|---|
| 串行 or 并行 | **串行（默认）** | ① 同一 `DeviceConnection` 下多设备**共享 `ConnectionSession`**，代码明确"串行化驱动访问"是既有约束，末位离场才 Dispose 驱动（`RuntimeManager.cs:588-589,620-628`）；并行注册会撞驱动 ② 单台注册是"先拆除再重建"的重流程，瞬间并发 N 条会打爆 PLC 侧连接上限 ③ 启动时 `InitializeAsync` 自己就是串行 `foreach`（`RuntimeManager.cs:238-241`），批量沿用同一节奏，行为一致 |
| 组间并行 | **可选** | 同区域可能跨多个 PLC（多个 `ConnectionId`），可"按 `ConnectionId` 分组、组内串行、组间并行"提速；每组仍受单连接串行约束，风险可控。**默认仍全串行，作为后续优化开关** |
| 事务 | **不包大事务** | ① 期望语义就是"部分成功"，一台失败不该回滚其他 ② EF Core `MySqlRetryingExecutionStrategy` 与手动事务冲突是项目已知坑 ③ 逐台单行更新天然原子 |
| 失败补偿 | **无需"失败回滚 IsEnabled"** | 已核实 `BuildAndRegisterDeviceAsync` 整体 `catch` 吞异常、只返回 `false`（`RuntimeManager.cs:873-877`）；连接失败走"占位重连 + Fault"（`:801-812`）**不抛异常** → 启用设备不存在"IsEnabled 落库但运行时未注册"的业务中间态，这是设计好的行为 |
| 批内节流 | 可配，默认每台间隔 50~100ms | 削峰，降低对同一 PLC 的连接冲击；**节流参数必须与 3.4 的"分批/任务化"决策联动**（节流 × 台数 + 注册耗时 = 总时长，决定是否超 HTTP 超时线） |
| 实现复用 | **不循环调 `SetEnabledAsync`，但抽公共核心** | 它有 N×3 查询放大 + 返回完整 DTO。见下节"防逻辑漂移" |

**统一服务方法**：

```csharp
// IDeviceAppService 新增
Task<BatchSetEnabledResultDto> BatchSetEnabledAsync(BatchSetEnabledRequest req);
Task<BatchSetEnabledPrecheckDto> PrecheckBatchSetEnabledAsync(BatchSetEnabledRequest req);
```

**逐台隔离**：每台包独立 `try/catch`，把 `SetEnabledAsync` 会抛的 `BusinessException`（地址未配置、设备不存在）捕获为 `items[].Failed`，**绝不冒泡中断整批** —— 这是修复现状问题 #1 的关键。

**防逻辑漂移（必须做）**：批量方法若从头写，会把"幂等判断 + 启动闸门"逻辑**再抄一遍**，两处必然漂移。应把单设备核心抽成内部方法，单设备与批量共用：

```csharp
// DeviceAppService 内：单设备 SetEnabledAsync 与批量 BatchSetEnabledAsync 共同调用
private async Task<BatchItemResult> SetEnabledCoreAsync(Device entity, bool enabled)
{
    // 1) 幂等判断（IsEnabled == enabled → Skipped）
    // 2) 启用时 ValidateVariablesConfiguredForStartAsync（地址闸门）
    // 3) entity.IsEnabled = enabled; UpdateAsync(entity)   // 单行更新
    // 4) enabled ? RegisterDeviceAsync(id) : RemoveDeviceAsync(id)
    // 5) 返回轻量结果（不含完整 DTO）
}
```

### 3.3 预检（本方案最值得加的一环）

把 `DeviceAppService.cs:950` 的私有校验 `ValidateVariablesConfiguredForStartAsync` 抽取为可批量调用的校验器（如 `IDeviceStartValidator`），预检一次性返回：

```jsonc
{
  "total": 24,
  "startable": 21,
  "blocked": [
    { "deviceId": 7, "name": "1#注塑机", "reason": "3 个已启用变量缺地址：温度、压力、转速" }
  ]
}
```

**前端交互**：点"批量启动"→ 先预检 → 弹窗展示"范围：XX 车间（含子区域）共 24 台 / 21 台可启动 / 3 台被阻塞（可展开看原因）"→ 用户选 **"仅启动可启动的 21 台"** 或 **取消**。

收益：把"点完一半报错"的糟糕体验消灭在点击之前，同时让 `skipInvalid` 有事实依据。

> **一致性窗口**：预检只是"点击时快照"，预检到执行之间设备可能被改（变量地址变了、设备被删）。因此服务端**执行时仍须逐台二次校验**（即 `SetEnabledCoreAsync` 里的地址闸门），把窗口期差异并入 `failed`；`skipInvalid` 只控制"是否因阻塞中断整批"，不取代执行时校验。

### 3.4 长耗时与进度（决策点）

**先估算总时长**：`每台注册耗时（几十~几百 ms）+ 批内节流（50~100ms）` × 台数。100 台可能到 **1~2 分钟**，已超同步 HTTP 的合理超时线。

| 规模 | 做法 |
|---|---|
| < 50 台 | 同步返回结果即可 |
| ≥ 50 台 | (a) **轻量**：保持同步接口，前端分批调用（每批 20 台）并聚合进度 ← **推荐**<br>(b) 任务化：返回 202 + `operationId`，后台执行，SignalR 新增 `ReceiveBatchProgress(operationId, done, total)` |

倾向 (a)：项目目前没有任务化基础设施，区域下设备规模通常在几十台，引入后台任务队列属于过度设计。**若预期单区域上百台，请选 (b)，且节流参数要相应放宽或去掉。**

### 3.5 前端设计

**入口**

- **入口 A（推荐，最贴合"整个区域"）**：区域树节点 hover 显示 ▷ / ⏸ 图标 → 直接对该区域批量操作，操作范围遵循树上的"含子区域"开关状态。
- **入口 B**：保留现有批量工具栏，把 `batchEnable/batchDisable` 的 for 循环换成**一次**批量调用；并补一个"全选当前筛选结果"按钮（现有 `toggleSelectAll` 只作用于当前可见集合，配合区域筛选其实已可用，但语义要说明白）。

两个入口共用同一个确认弹窗组件，只是目标不同（`{areaId, includeSubAreas}` vs `{deviceIds}`）。

**交互流水线**

```
点击（区域节点 ▷ / 工具栏"批量采集"）
  → 预检请求（仅启用时；停用直接进确认）
  → 确认弹窗：范围说明 + 可启动数 + 阻塞清单（可展开）
  → [仅启动 N 台] / [取消]
  → 执行请求（按钮 loading + 禁用，顶部进度条）
  → 结果弹窗：成功 X / 跳过 Y / 失败 Z，失败可展开原因
  → 失败项保留选中，提供 [重试失败项]（用 deviceIds 路径重发）
  → 一次性刷新设备列表（不要 N 次刷新）
```

**反馈语义（重要）**：结果弹窗只做"**提交结果**"汇总，不代表"已在线"。设备真实在线状态以 SignalR `ReceiveDeviceStatus` 为最终真相——停用会即时 Offline，启用要等建连数秒（失败走占位重连 + Fault）。进度条文案应写"正在下发 N 台…"而非"正在启动…"，避免与设备在线状态冲突。

**改造点**

| 位置 | 改动 |
|---|---|
| `Client/src/api/deviceApi.ts` | 新增 `batchSetDeviceEnabled(req)`、`precheckBatchSetDeviceEnabled(req)`；保留 `setDeviceEnabled` 给单设备用 |
| `Client/src/services/deviceService.ts` | 现有 `setDeviceEnabledAndSync` 保留；新增批量版本，完成后统一同步一次 |
| `DeviceManagementView.vue:264-292` | `batchEnable/batchDisable` 改为单次批量调用 + 结果处理；`batchEnable` 补 `confirm`（对齐 `batchDisable`） |
| `DeviceManagementView.vue` 模板 | 新增确认弹窗 + 结果弹窗（可抽 `BatchDeviceOperationDialog.vue`） |
| 区域树组件 | 节点 hover 操作入口，emit `batchToggle(areaId, enabled)` |
| 状态刷动（可选） | 批量期间对 `ReceiveDeviceStatus` 做短暂 UI 缓冲，避免列表连续闪动 |

---

## 4. 风险与对策

| 风险 | 事实依据 | 对策 |
|---|---|---|
| 并发重复操作同一设备 | `DeviceRepository.GetByIdForUpdateAsync`（`:43`）**只是跟踪查询、不加行锁**，批量与单设备操作可能交错 | ① `RegisterDeviceAsync` 先 `RemoveDeviceAsync` 再重建 → **最终以最后一次为准，天然最终一致**（幂等只防"状态无变化重复注册"，防不住并发写，但结果收敛）② 前端批量执行期间禁用入口 ③ 可选 `operationId` 去重 |
| 目标设备在校验后被删除 | 区域/设备可被并发删除 | 捕获"设备不存在"归入 `failed`，不中断整批 |
| 大区域执行久、HTTP 超时 | 串行 + 节流 | 分批调用（3.4a）或任务化（3.4b）；服务端设置合理超时；节流与规模联动 |
| 状态推送风暴 | 每台注册/注销各推一次 | 结果弹窗做提交汇总；真实状态交给 SignalR；前端可选 UI 缓冲 |
| 启用反馈慢于停用 | 建连耗时 + 占位重连 | 进度条文案与"在线状态"解耦（3.5 反馈语义） |
| 审计动态明细落不进去 | `[AuditLog]` 为固定 action 特性 | 扩展特性支持动态 detail，或 service 手写一条 |
| 术语混淆 | `IsEnabled` vs `RunState` | 见第 1 节，实施前确认 |
| Operator 越权 | 角色隔离方案 | 复用 `RequireAdmin` 策略 |

---

## 5. 决策点汇总（请逐条确认）

| # | 决策点 | 选项 | 我的建议 |
|---|---|---|---|
| 0 | **方案分档** | A 纯前端 / B 后端批量+预检 | ✅ **B**（已确认） |
| 1 | **语义确认** | IsEnabled 采集使能 / RunState 运行态控制 | ✅ **IsEnabled**（已确认） |
| 2 | 预检 + 部分成功 | 要 / 不要 | ✅ **要**（已确认） |
| 3 | 长耗时策略 | (a) 同步分批 / (b) 任务化 + SignalR 进度 | ✅ **(a) 同步分批**（规模 50~100 台，已确认） |
| 4 | 批量操作记录表 | 建表 / 不建 | ✅ **不建**（已确认，失败重试由前端持 `deviceIds` 实现） |
| 5 | 入口范围 | 仅区域树 / 区域树 + 工具栏都做 | ✅ **都做**（已确认，共用弹窗） |
| 6 | 组间并行 | 全串行（先上）/ 按 `ConnectionId` 分组并行 | ✅ **全串行先上**（已确认） |

> 批内节流（默认 50~100ms）与"抽 `SetEnabledCoreAsync` 防漂移"不进决策点，作为**实现要求**默认执行。

---

## 6. 实施拆解（批准后再动代码）

| # | 层 | 内容 |
|---|---|---|
| 1 | Domain / Application | `BatchSetEnabledRequest`、`BatchSetEnabledResultDto`、`BatchSetEnabledPrecheckDto`；`IDeviceAppService` 增两个方法；抽 `IDeviceStartValidator`（由 `DeviceAppService.cs:950` 私有方法提升） |
| 2 | Application | ① 抽 `SetEnabledCoreAsync(entity, enabled)`，让单设备 `SetEnabledAsync` 与批量共用（防漂移）② `BatchSetEnabledAsync`：`ResolveTargetsAsync` → 预检 → 串行执行（逐台 try/catch + 节流）→ 聚合结果 |
| 3 | WebApi | `DeviceController` 两个端点 + `RequireAdmin` + 单条审计（扩展 `AuditLog` 动态 detail 或手写） |
| 4 | Client | `deviceApi.ts` / `deviceService.ts` 增批量方法；`DeviceManagementView.vue` 换批量逻辑；新增确认/结果弹窗；区域树加入口 |
| 5 | 验证 | 单区域批量启用/停用、含子区域、部分失败、幂等重复点击、Operator 403、批量期间 SignalR 状态一致性、停用不触发预检 |

---

## 附录：改动前后对比（档位 B）

| 维度 | 改动前 | 改动后 |
|---|---|---|
| 请求次数 | N 次（每台一次） | **1 次** |
| DB 查询 | ~N×3 + N 次校验 | 1 次批量加载 + N 次单行更新 |
| 中途失败 | 中断整批，选中态残留 | **逐台隔离**，返回完整失败清单 |
| 可启动性预判 | 无 | **预检弹窗**，点击前可知 |
| 二次确认 | 启停不对称（启用无） | 统一确认弹窗 |
| 审计 | N 条 | **1 条**（含失败明细） |
| 数量级感知 | 无 | 进度条 + 结果汇总 + 失败重试 |
| 单/批量逻辑 | — | 共用 `SetEnabledCoreAsync`，不漂移 |
| 失败补偿 | — | 无需（运行时吞异常、走占位重连） |

---

## 修订记录

- **v2（2026-09-21）**：并入复核结论 —— ① 新增第 0 节"方案分档 A/B"；② 更正"失败回滚"为"无需补偿"（`BuildAndRegisterDeviceAsync` 吞异常）；③ 更正并发对策为"最终一致"（幂等不防并发写）；④ 补 `SetEnabledCoreAsync` 防逻辑漂移；⑤ 补 precheck/skipInvalid 仅对启用有意义；⑥ 补预检一致性窗口；⑦ 补启用/停用反馈节奏、skipped 双原因、审计动态明细、节流与规模联动；⑧ 新增组间并行决策点。
- **v1（2026-09-21）**：初版。
