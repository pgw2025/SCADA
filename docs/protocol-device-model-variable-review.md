# 前后端协议 / 设备 / 数据模型 / 变量关联梳理与问题清单（评审稿，待确认）

- **状态**：分析完成，待用户确认后执行（**本文档不改动任何代码**）
- **日期**：2026-08-24
- **范围**：前端（`Client/`）与后端（`Server/`）之间，协议（DeviceType）、设备（Device）、数据模型（DataModel）、变量（ModelVariable）、变量运行时值、HMI 界面绑定的关联链路
- **目标**：把"协议/设备/模型/变量/界面"五者的真实关联方式画清楚，并找出其中结构性 / 一致性 / 正确性风险，按严重度排序

---

## 0. 一页速览（关联链路总图）

```
                    ┌─────────────────────────────────────────────┐
                    │              前端 types.ts                    │
                    │  Device { type: DeviceType, modelId, ... }    │
                    │  DataModel { type: DeviceType, variables[] }  │
                    │  ModelVariable { dataType, type, ... }        │
                    │  HMIComponent { bindField: 变量Key }           │
                    └───────────────┬───────────────┬───────────────┘
                                    │ HTTP/SignalR   │ 按 modelId 反查
                                    ▼               ▼
       后端 REST (DeviceController)          后端 DataModelController
       DeviceDto: 无 status 字段              DataModel: 无 Type 字段(已改为 VendorModel)
       Device.Type = 协议真相源               前端反查: devices.find(d=>d.modelId)==type
                                    │
                                    ▼
       后端 RuntimeManager.InitializeAsync
       Device → CreateDriver(Device.Type) → IProtocolDriver
              → Model = GetById(ModelId)
              → Variables = GetList(ModelId)
              → VariableRuntime { Variable, Value, Quality }
                                    │
                                    ▼  SignalR: ReceiveVariableUpdate(variableKey, value)
       前端 signalRService 按 variableKey 写入 dev.variables[key]
                                    │
                                    ▼  HMI 组件 bindField === variableKey → 渲染实时值
```

**结论先行**：五者关联在"设备↔模型↔变量↔运行时值↔HMI"这条主链上是通的；**最大的结构性问题集中在"协议类型（DeviceType）"在前端被重复编码、且与后端枚举时序不一致，以及"设备状态"全链路缺失（后端不返回、SignalR 不推送、前端永远 offline）**。

---

## 1. 关联方式逐一拆解

### 1.1 协议（DeviceType）—— 后端唯一真相源，前端三处各编一份
- 后端：`Device.Type`（`DeviceType` 枚举，7 种：S7/ModbusTcp/OpcUa/Mqtt/Virtual/BACnet/DNP3）。`RuntimeManager.InitializeAsync` 用 `CreateDriver(device.Type)` 选驱动。**这是协议的唯一真相源**（与 `docs/device-attribute-refactoring-design.md` 决策一致，`DataModel.Type` 已改为纯描述性 `VendorModel`）。
- 前端 `types.ts:128`：`DeviceType = 'OPCUA' | 'S7' | 'MQTT' | 'Virtual'` —— **只 4 种**，缺 ModbusTcp/BACnet/DNP3。
- 前端 `DataModelView.vue:48/543`：新建模型时仍让用户选 `modelType: DeviceType`（OPCUA/S7/MQTT/Virtual），但该字段**后端已不存储**（DataModel 无 Type 列），实际协议靠"绑定到该模型的设备"反查（见 1.2）。
- 前端 `LiveDataView.vue`、`DeviceManagementView.vue`：设备类型筛选/徽标写死 `'OPCUA'|'S7'|'MQTT'|'Virtual'`。

### 1.2 设备 ↔ 数据模型（Device ↔ DataModel）—— 通过 modelId 关联，协议反查隐含依赖
- 后端：`Device.ModelId → DataModel`；`DataModel` 实体**已无 Type 字段**（改为 `VendorModel` 描述）。
- 前端 `DataModelView.vue:32-42`：`currentModelProtocol` 通过 `devices.find(d => d.modelId === model.id)?.type` 反查协议。**隐含强依赖**：若某模型尚未绑定任何设备，协议回退 `'Virtual'`，界面下拉的 dataType 选项随之错误（如本应是 S7 点位却显示 Virtual 的 INT/FLOAT）。

### 1.3 数据模型 ↔ 变量（DataModel ↔ ModelVariable）—— 通过 modelId，前端依赖 DataConversion 同步
- 后端：`ModelVariable.ModelId → DataModel`；运行时 `RuntimeManager` 按 `ModelId` 拉全部变量装入 `DeviceRuntime.Variables[variableId]`。
- 前端：`DataModel.variables: ModelVariable[]`（前端本地维护一份变量数组，由 `DataModelAppService` 提供）；新建/删除变量时前端**手动增量同步**到 `dataModels` 与所有 `devices[].variables`（见 `DataModelView.vue:268-276`）。**前端变量 map 与后端存在"双份真相"**：后端以 modelId 为准，前端以本地数组 + 手工 push 为准，无单测保障增量同步不漏。

### 1.4 变量 ↔ 运行时实时值（ModelVariable ↔ VariableRuntime.Value）—— 通过 variableKey 桥接，前后端 key 命名风险
- 后端：`VariableRuntime.Variable`（ModelVariable 定义）+ `Value`/`Quality`/`UpdateTime`；`DeviceRuntime.Variables` 以 **variableId(int)** 为键。
- 信号推送：`ScadaHub` 目前**只推送 `ReceiveVariableUpdate(variableKey, value)`**，按**变量 Key（字符串）**下发（见 `signalRService.ts:27` 与 `ScadaHub.cs`）。→ **后端用 Id 索引、前端/SignalR 用 Key 索引**，两套键并存，靠命名约定维系。
- 前端 `signalRService.ts:27-42`：收到推送后遍历所有 device，`dev.variables[variableKey] = newValue`。**风险**：变量 Key 若不全局唯一（仅 modelId 内唯一），跨设备同名 key 会被错误写入多台设备。

### 1.5 变量 ↔ HMI 界面显示（ModelVariable ↔ HMIComponent）—— bindField 直接等于变量 Key
- 前端 `types.ts:30`：`HMIComponent.bindField` 注释为 "The simulation variable key"。
- 渲染：`HMIWidget.vue` / `CanvasPanel.vue` 用 `bindField` 去 `devices[].variables[bindField]` 取值（运行时实时值）。**绑定是"裸 key 字符串匹配"，无设备维度限定**——若一个页面绑定了多台设备的同名 key，取值会落到"遍历到的第一台"设备，存在歧义。
- 变量静态属性（dataType/unit/min/max）来自 `DataModel.variables`，运行时值来自 `dev.variables[key]`，**两者靠 key 拼接，前端无类型校验**：若 DataModel 里删了某变量但页面组件仍 bindField 引用它，界面会静默显示空值（无报错、无校验清单）。

### 1.6 设备状态（Device ↔ 实时状态）—— 全链路缺失（最严重）
- 后端：`Device` 实体**无状态列**；`DeviceRuntime` 有运行状态但**仅内存**；`ScadaHub` 未推送任何设备状态；`DeviceController` 无状态查询接口（`UpdateDeviceStatusDto` 定义了却**从未被控制器使用**，见 `DeviceController.cs` 与 `DeviceDtos.cs`）。
- 后端 `DeviceStatus` 枚举定义了 Offline/Online/Fault/...，但**没有任何代码把运行态写回 Device 或推送给前端**。
- 前端：`Device.status` 字段在 `types.ts:150` 声明（`number|string`），但后端 `DeviceDto`（`DeviceAppService.GetListAsync/GetByIdAsync`）**根本不返回 status**。前端设备列表/详情的"在线绿点"判断 `dev.status===1||'online'` 永远为 false → **所有设备界面上恒显"离线/灰色"**，且离线时写入按钮被锁定（见 `LiveDataView.vue:386`），即**实时写值功能在前端永远不可用**。
- 这与 `docs/device-model-fix-plan.md`「问题二」描述一致，但现状比文档更严重：**文档假设"前端从运行时接口取 status"，而实际上该接口/SingalR 推送都不存在**。

---

## 2. 问题清单（按严重度排序）

| # | 严重度 | 问题 | 现状（代码实证） | 影响 |
|---|--------|------|------------------|------|
| P0-1 | **严重** | **设备状态全链路缺失**：后端不返回、SignalR 不推送、前端永远离线 | `DeviceAppService` 返回 DeviceDto 无 status；`ScadaHub` 无状态推送；`DeviceController` 无状态接口；`UpdateDeviceStatusDto` 定义的但没人用；前端 `Device.status` 恒 undefined | 所有设备界面恒显"离线"，**前端"写入/强制"功能永远被锁定无法使用**；运维无法从界面判断设备真实连接态 |
| P0-2 | **严重** | **DeviceType 前后端不一致**：后端 7 种，前端只 4 种 | 后端 `DeviceType.cs` 7 种；前端 `types.ts:128` 仅 OPCUA/S7/MQTT/Virtual；`DataModelView`/`LiveDataView`/`DeviceManagementView` 筛选写死 4 种 | ModbusTcp/BACnet/DNP3 设备在前端下拉/筛选落入 undefined，无法正确渲染协议配置表单；且后端工厂这三者 `throw NotSupportedException`（驱动未实现） |
| P0-3 | **中等** | **变量 Key 全局唯一性假设错误**：SignalR 按 key 写值，但 key 仅 modelId 内唯一 | `RuntimeManager` 以 variableId 索引；SignalR/前端以 key 索引（`signalRService.ts:29`）；`ModelVariable.Key` 无跨设备唯一约束 | 同名 key 跨设备时，推送值被错误写入多台设备；HMI bindField 同名 key 绑定歧义 |
| P1-1 | 中等 | **DataModel.Type 前端残留**：前端仍让用户选模型协议类型，后端已不存 | `DataModelView.vue:48/543` 用 `modelType: DeviceType` 发请求；后端 `DataModel` 无 Type 列（已改 VendorModel） | 用户选的模型协议类型被静默丢弃；界面协议徽标靠"反查绑定设备"得出，未绑设备时错显 Virtual |
| P1-2 | 中等 | **前端变量 map 双份真相**：本地数组 + 手工增量同步，无保障 | `DataModelView.vue:268-276` 新建/删除变量时手动 push/delete 到 `devices[].variables` | 同步遗漏会导致界面变量与实际模型不一致；分布式/多 tab 下易漂移 |
| P1-3 | 中等 | **HMI 绑定缺设备维度 + 无失效校验**：bindField 裸 key，删变量后静默空值 | `HMIComponent.bindField` 直接等于 key；取值无设备限定、无"变量已删除"告警 | 同名 key 跨设备歧义；组件引用已删变量时静默显示空，难以排查 |
| P2-1 | 低 | **驱动未实现却被枚举暴露**：ModbusTcp/Mqtt 后端工厂直接 throw | `ProtocolDriverFactory.cs:40-56` | 即便前端补齐类型，采集也会在运行时抛异常；应在创建设备时前置拦截（已在 refactoring-design 列为 P2-9，尚未做） |
| P2-2 | 低 | **前端枚举命名与后端序列化潜在不一致**：前端 `'OPCUA'/'S7'/'MQTT'/'Virtual'` 全大写，后端枚举名 `OpcUa`/`S7`/`Mqtt`/`Virtual` 大小写混合 | 前端类型字面量 `'OPCUA'` vs 后端 `OpcUa`；需确认 JSON 序列化策略（目前靠 DataModel 创建时后端不校验 type、Device.Type 由表单发字符串，疑似大小写未对齐） | 序列化/反序列化大小写不匹配可能导致 400 或存储值偏差 |
| P2-3 | 低 | **文档与实际代码漂移**：refactoring 文档称 DeviceRuntime 含 CurrentStatus/LastError/ReconnectCount，实际 DeviceRuntime.cs 无这些字段；文档称已删 DriverName，代码确已删；文档称 ModelVariable.Type 已派生，代码确已派生 | `DeviceRuntime.cs` 仅含 ConnectionState/IsRunning/统计计数，无 CurrentStatus 等 | 文档作为后续改造依据时，会误导实施者 |

---

## 3. 改造前后对比（重点项）

### 3.1 设备状态（P0-1，优先级最高）
**Before**：后端无状态返回 + SignalR 无推送 + 前端恒离线 + 写值锁死。
**After（建议）**：
1. `Device` 新增持久化 `LastKnownStatus`（`DeviceStatus` 枚举，默认 Offline），Runtime 层状态变更节流写回。
2. `DeviceController` 新增 `GET /api/Device/{id}/status` 或 `GET /api/Device/statuses` 批量返回实时态（从 RuntimeManager 内存读）。
3. `ScadaHub` 新增 `ReceiveDeviceStatus(deviceId, status)` 推送，前端 `signalRService` 订阅并写回 `dev.status`。
4. 前端 `Device.status` 拆分为 `status`（实时，来自运行时/推送）与 `lastKnownStatus`（持久，重启兜底）。
> 注：此改动涉及一次 EF 迁移（新增列带默认值），可逆。

### 3.2 DeviceType 前后端对齐（P0-2）
**Before**：后端 7 种，前端 4 种，筛选/下拉写死 4 种。
**After**：
1. 前端 `types.ts` 扩为全量 7 种，大小写与后端序列化值对齐（确认 `OpcUa` vs `OPCUA`）。
2. 前端三处筛选/下拉（DeviceManagementView / LiveDataView / DataModelView 协议反查）改为基于统一常量数组，避免散落硬编码。
3. 后端 `CreateDriver(string)` 路径（仍并存）的大小写映射与枚举对齐，并在 `CreateAsync` 前置校验 `IsSupported`（未实现协议拒绝创建设备，而非运行时抛异常）。

### 3.3 变量 Key 唯一性（P0-3）
**Before**：key 仅 modelId 内唯一，SignalR/前端按 key 全局写值。
**After**：
- 方案 A（推荐）：SignalR 推送改为 `(deviceId, variableKey, value)`，前端按设备定位写入，消除跨设备串写。
- 方案 B：约束 `Key` 全局唯一（唯一索引），HMI bindField 改为 `deviceKey.variableKey` 复合键。改动更大，建议与 HMI 绑定改造（P1-3）一并做。

---

## 4. 待你确认的事项（确认后再动手）
- [ ] P0-1 设备状态：是否接受「新增 `LastKnownStatus` 列 + 状态接口 + SignalR 推送」三件套？还是先做最小化（仅 SignalR 推送 + 前端写回，不落库）？
- [ ] P0-2 DeviceType：前端补齐 7 种时，后端枚举序列化大小写以哪个为准（建议后端统一输出与前端一致的字符串，避免前端散改）？
- [ ] P0-3 变量 Key：选方案 A（推送带 deviceId，改动小）还是方案 B（全局唯一 + HMI 复合键，改动大但更彻底）？
- [ ] P1-1/P1-2/P1-3：是否纳入本轮一起治理，还是先聚焦 P0？
- [ ] 是否要我顺手修正 `docs/device-attribute-refactoring-design.md` 中与代码漂移的段落（P2-3），使其作为后续改造依据时准确？

---

## 5. 关键证据文件索引
- 后端协议真相源：`Server/ScadaServer.Domain/Entities/Device.cs:48`（`Type`）、`DataModel.cs:29`（`VendorModel`）
- 后端驱动选择：`Server/ScadaServer.Infrastructure/Communication/ProtocolDriverFactory.cs`（枚举路径 + string 路径，ModbusTcp/Mqtt 未实现）
- 后端运行时装配：`Server/ScadaServer.Runtime/RuntimeManager.cs:82-109`（CreateDriver(Type) + 按 ModelId 拉变量）
- 后端状态缺失：`Server/ScadaServer.Application/Services/DeviceAppService.cs:66-84`（DeviceDto 无 status）、`ScadaHub.cs`（仅变量推送）、`DeviceController.cs`（无状态接口）
- 前端类型定义：`Client/src/types.ts:90-167`（DeviceType/Variables/Device.status）
- 前端协议反查：`Client/src/components/DataModelView.vue:32-42`
- 前端 SignalR 写值：`Client/src/services/signalRService.ts:27-42`（按 variableKey 写入 dev.variables）
- 前端状态显示锁写值：`Client/src/components/LiveDataView.vue:180,214,386`（status 恒 false → 离线 + 写值锁定）
