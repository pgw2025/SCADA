# P0 修复方案：协议/状态/变量 Key 三大结构性问题

- **状态**：方案稿，待用户确认后执行（**本文档不改动任何代码**）
- **日期**：2026-08-24
- **范围**：仅 P0 级（P0-1 设备状态缺失、P0-2 DeviceType 前后端不一致、P0-3 变量 Key 全局唯一假设错误）
- **前置结论（已代码核实）**：
  - 后端 `DeviceTypeJsonConverter` + `DataTypeEnumJsonConverter` 已在 `Authentication.Extensions.cs:27-29` **全局注册**。`DeviceDto.Type` 序列化输出为转换器字符串（`OPCUA`/`S7`/`MQTT`/`Virtual`/`ModbusTcp`/`BACnet`/`DNP3`），读取大小写不敏感。→ **P2-2 大小写问题后端侧已解决**，前端不必散改大小写。
  - 后端 `DeviceDto` 已预留 `RuntimeStatus`（`DeviceStatus?`，`DeviceDto.cs:54`），但 `DeviceAppService.GetListAsync/GetByIdAsync` **完全没有给它赋值**——状态链路缺的是"填充 + 推送 + 前端消费"，DTO/枚举已就绪。
  - 运行时连接态用 `DeviceConnectionState`（Unknown/Connecting/Connected/Disconnected/Error/Initializing），与对外 `DeviceStatus`（Offline/Online/Fault/...）是两个枚举，需在填充时做映射。

---

## P0-1 设备状态全链路打通（优先级最高）

### 问题实证
- 后端：`DeviceDto` 有 `RuntimeStatus` 但未填充；`DeviceController` 无状态接口；`ScadaHub` 仅推送 `ReceiveVariableUpdate`，无设备状态推送；`DeviceRuntime.ConnectionState`/`IsRunning` 等仅内存。
- 前端：`Device.status` 恒 `undefined` → `LiveDataView.vue:180/214/386` 恒判离线 → **在线绿点永远灰色、写入按钮永远锁定**。

### 修复方案（分三层，可灰度）

**A. 后端 AppService 填充 RuntimeStatus（必做，零 schema 改动）**
- `RuntimeManager` 暴露 `GetRuntimeStatus(int deviceId)` 方法，从 `DeviceRuntimes[deviceId].ConnectionState` 读内存态。
- `DeviceAppService` 注入 `IRuntimeManager`，在 `GetListAsync` / `GetByIdAsync` 映射 `DeviceDto` 时按 `device.Id` 填 `RuntimeStatus`：
  - `Connected` → `Online`；`Connecting`/`Initializing` → `Connecting`（映射到 DeviceStatus 需新增或复用 `ConfigUpdating`/`Connecting`）；`Disconnected`/`Error` → `Offline`；未在运行时 → `Offline`（保守）。
- 这样**现有 REST 轮询（pollService 每 5s）即可拿到状态**，前端无需改通道即可显示在线/离线。

**B. SignalR 实时推送（推荐，体验提升）**
- `ScadaHub` 新增 `ReceiveDeviceStatus(int deviceId, DeviceStatus status)`。
- `DeviceScheduler`/`DeviceWorker` 在连接态变更（Offline↔Online、进入 Error）时调用 `Clients.All.SendAsync("ReceiveDeviceStatus", ...)`；加节流（如状态不变不重复发）。
- 前端 `signalRService.ts` 新增 `connection.on("ReceiveDeviceStatus", (id, status) => { 更新 devices.value 中对应设备 })`；`Device.status` 改为取自推送/`RuntimeStatus`。

**C. 持久化最后已知状态（可选，重启不丢）**
- `Device` 表新增 `LastKnownStatus`（`int`，默认 0=Offline）+ EF 迁移（带默认值，可逆）。
- 状态变更时 Runtime 层节流写回 `Device.LastKnownStatus`；`DeviceAppService` 读取时若该设备不在运行时，回退 `LastKnownStatus`。
- 前端离线兜底：`status` 实时来自 B/C，若两者皆无则显示 `lastKnownStatus`。

### 前端配套改动
- `types.ts`：`Device.status` 语义明确为"实时态"；新增 `lastKnownStatus?: number`。
- `LiveDataView.vue:180/214/386`：`dev.status` 直接用后端 `RuntimeStatus`/推送值（已是 `DeviceStatus` 数值，0/1/2...），去掉对 `'online'` 字符串的兼容判断（或保留兼容）。
- `signalRService.ts`：订阅 `ReceiveDeviceStatus`。

### 影响面 / 风险
- A 无 DB 改动、最低风险，**先做 A 即可解锁"在线显示 + 写入按钮"**。
- B 增加 SignalR 消息量，需节流。
- C 需一次 EF 迁移（新增列带默认值），可逆。

---

## P0-2 DeviceType 前后端对齐（7 种）

### 问题实证
- 后端枚举 7 种（`DeviceType.cs`）；全局转换器已能正确序列化/反序列化 7 种。
- 前端 `types.ts:128` 仅 `OPCUA | S7 | MQTT | Virtual`，缺 `ModbusTcp | BACnet | DNP3`。
- 前端三处硬编码 4 种：`DeviceManagementView`、`LiveDataView`（筛选 `['ALL','OPCUA','S7','MQTT','Virtual']`）、`DataModelView`（协议反查与下拉）。
- 后端 `ProtocolDriverFactory` 对 `ModbusTcp`/`Mqtt` 直接 `throw NotSupportedException`（驱动未实现）。

### 修复方案

**A. 前端类型扩为 7 种，字面量对齐后端转换器输出**
```ts
// types.ts
export type DeviceType = 'S7' | 'ModbusTcp' | 'OpcUa' | 'Mqtt' | 'Virtual' | 'BACnet' | 'DNP3';
```
- 注意：转换器 SerializeMap 输出 `OpcUa`（非 `OPCUA`）、`ModbusTcp`/`BACnet`/`DNP3` 为 PascalCase。→ **前端字面量必须与之一致**（`OpcUa` 而非 `OPCUA`）。需同步修正现有 `OPCUA`→`OpcUa`、`MQTT`→`Mqtt` 的所有引用（types.ts / DeviceManagementView / LiveDataView / DataModelView / pollService 等），否则既有设备 type 比对会失败。
- 替代（更省改动）：若希望前端保持全大写风格，可改后端 `DeviceTypeJsonConverter.SerializeMap` 输出为 `OPCUA`/`MQTT` 等全大写。**推荐改前端字面量对齐后端**（后端已全局一致，前端统一更干净）。

**B. 统一设备类型下拉/筛选项为常量数组**
- 新增 `Client/src/constants.ts`（或 types.ts 内 `DEVICE_TYPES` 数组），三处视图从常量渲染，杜绝散落硬编码与遗漏。

**C. 协议配置表单补齐 ModbusTcp/BACnet/DNP3 分支**
- `DeviceManagementView.vue` 的 `buildConfigJson` 与表单 `v-if` 分支补齐三种协议的连接参数（参考 `CreateDeviceDto.cs:44-47` 的 JSON 模板：`ModbusTcp: {IpAddress,Port,UnitId}` 等）。
- 若驱动尚未实现，在表单/下拉上明确标注"暂不支持采集"，避免用户误建后初始化失败。

**D. 后端前置拦截未实现协议（与 refactoring-design P2-9 一致）**
- `DeviceAppService.CreateAsync` 调 `IProtocolDriverFactory.IsSupported(type)`（或工厂新增 `IsSupported`），未实现则抛 `BusinessException` 拒绝创建，而非运行时初始化才抛异常。

### 影响面 / 风险
- A/B/C 纯前端，无 DB 改动；D 为后端校验增强。
- 主要风险：**字面量大小写改漏**会导致已有设备 type 比对失败（设备列表筛选/协议反查错乱）。需全局 grep `OPCUA`/`MQTT`/`S7`/`Virtual` 一并替换。

---

## P0-3 变量 Key 全局唯一假设错误

### 问题实证
- 后端：`ModelVariable.Key` 仅 `ModelId` 内唯一（`RuntimeManager` 以 variableId 索引）。
- SignalR/前端：`signalRService.ts:27-42` 按 `variableKey` 全局遍历所有 device 写入 `dev.variables[key]`。
- 跨设备同名 key 会被错误串写；HMI `bindField` 裸 key 绑定存在歧义（见 P1-3）。

### 修复方案（推荐方案 A，最小改动）

**A. SignalR 推送携带 deviceId（推荐）**
- 后端 `IProtocolDriver` 的 `onValueChanged` 回调目前是 `Action<string, object>`（key, value）。改为 `Action<int, string, object>`（deviceId, key, value），或在 `DeviceWorker` 发推送时带上 `runtime.Device.Id`。
- `ScadaHub`：`ReceiveVariableUpdate(int deviceId, string variableKey, object value)`。
- 前端 `signalRService.ts`：按 `deviceId` 定位到具体设备再写 `dev.variables[key]`，消除跨设备串写。
- 影响：需改驱动接口签名（S7/OpcUa/Virtual/Mqtt 四处的 `onValueChanged` 调用）与 `DeviceWorker` 订阅代码。

**B. Key 全局唯一 + HMI 复合键（更彻底，改动大）**
- `ModelVariable.Key` 加全局唯一索引；创建/导入时校验。
- HMI `bindField` 改为 `deviceKey.variableKey` 复合键，前端取值按设备定位。
- 与 P1-3（HMI 绑定改造）一并做。建议**本轮先做 A 止血，B 留待 P1 阶段**。

### 影响面 / 风险
- A 改驱动接口签名，需同步 4 个驱动 + DeviceWorker，但逻辑简单、向后兼容（旧前端收 2 参会忽略多余参数，但建议前后端同步发版）。
- B 涉及 DB 唯一索引 + 历史数据校验 + HMI 大改，成本高。

---

## 实施顺序建议（P0 内部）
1. **P0-1-A**（AppService 填 RuntimeStatus）—— 零 DB 改动，立即解锁在线显示与写入按钮，优先级最高。
2. **P0-2-A/B/C**（前端 7 种类型 + 常量化 + 表单分支）—— 纯前端，消除类型黑洞。
3. **P0-3-A**（SignalR 带 deviceId）—— 改驱动接口，止血跨设备串写。
4. **P0-1-B/C**（SignalR 推送 + 持久化）—— 体验与重启兜底，可紧随或稍后。
5. **P0-2-D**（后端前置拦截未实现协议）—— 防御性增强。

---

## 待你确认的事项
- [ ] P0-1：是否接受「先 A（REST 填状态，零 DB）立刻解锁，再 B（推送）/C（落库）」的灰度顺序？还是一步到位做 B/C？
- [ ] P0-2 大小写：前端字面量对齐后端（`OpcUa`/`Mqtt`/`ModbusTcp`/`BACnet`/`DNP3`），还是反过来改后端转换器输出为全大写？
- [ ] P0-3：选方案 A（推送带 deviceId，改动小）还是方案 B（Key 全局唯一 + HMI 复合键，彻底但大改）？
- [ ] ModbusTcp/BACnet/DNP3 驱动未实现：前端是否仍展示这三种（标注"暂不支持采集"），还是先只展示已实现的 4 种？
