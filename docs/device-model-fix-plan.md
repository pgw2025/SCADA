# 设备属性设计修复方案（高优先级：一致性/正确性风险）

> 状态：待用户确认后执行
> 范围：仅修复"高优先级（一致性/正确性风险）"三类问题，不涉及中/低优先级优化
> 原则：不破坏现有数据库数据、不改接口契约语义、前后端同步

---

## 问题一：前端 DeviceType 仅覆盖 4 种，后端有 7 种

### 现状
- 后端枚举 `DeviceType.cs`：S7(1) / ModbusTcp(2) / OpcUa(3) / Mqtt(4) / Virtual(5) / BACnet(6) / DNP3(7)
- 前端 `Client/src/types.ts:128`：`export type DeviceType = 'OPCUA' | 'S7' | 'MQTT' | 'Virtual'`
- 缺失：ModbusTcp、BACnet、DNP3

### 风险
ModbusTCP / BACnet / DNP3 设备在前端会落入 `undefined` 或无法被下拉框识别，配置表单无法渲染对应字段。

### 修复方案
1. **前端 `types.ts`**：把 `DeviceType` 扩为全量 7 种，与后端枚举值字符串对齐：
   ```ts
   export type DeviceType = 'S7' | 'ModbusTcp' | 'OpcUa' | 'Mqtt' | 'Virtual' | 'BACnet' | 'DNP3';
   ```
   - 注意大小写：`ModbusTcp` / `OpcUa` / `BACnet` / `DNP3` 需与后端序列化值一致（建议后端确认 JSON 序列化输出，必要时统一策略）。
2. **前端协议配置表单**：补充 ModbusTcp / BACnet / DNP3 的连接参数表单分支（参考 `CreateDeviceDto.cs` 中已有 JSON 模板）。
3. **驱动的 `ProtocolDriverFactory`**：确认三者驱动已实现（据调研后端枚举已定义，前端未暴露）。若后端驱动未实现，需在方案阶段标注为"前端可配置但采集暂不支持"，避免误导。

### 影响面
- 前端：types.ts、deviceApi、device 配置表单组件
- 后端：无 schema 改动（枚举已存在），仅确认序列化对齐

---

## 问题二：设备状态双份来源（Device 无状态列，DeviceRuntime 仅内存，前端又期望 Device.status）

### 现状
- `Device.cs` 表无状态字段；状态存在于 `DeviceRuntime`（运行时内存，含 CurrentStatus / LastHeartbeat / ReconnectCount / LastError）。
- 前端 `Device.status: number | string` 期望设备有状态值。
- 后端 `Device.LastCommunicationTime` 仅"记录用"，不表达实时状态。

### 风险
状态来源不唯一：前端从哪个接口拿状态？重启后 DeviceRuntime 清零、历史故障次数丢失；前端看到的 status 与真实连接态可能长期不一致。

### 修复方案
采用"**持久化最后已知状态 + 运行时实时状态**"双轨，明确职责：

1. **Device 表新增持久化状态列**（EF 迁移）：
   - `LastKnownStatus` (int，对应 DeviceStatus 枚举，默认值 Offline=0)
   - 含义：设备最后一次被持久化确认的状态，进程重启后不丢。
2. **运行时状态** 仍由 `DeviceRuntime` 承载（CurrentStatus / LastError / ReconnectCount 等），作为实时真值，通过 SignalR / 轮询接口暴露给前端。
3. **前端取值规则明确化**：
   - 实时状态 → 从运行时接口（SignalR 推送或 `/api/Device/status`）读取 `CurrentStatus`。
   - 离线/兜底状态 → 使用 `Device.lastKnownStatus`（重启后仍有值）。
   - 废弃前端直接把 `Device.status` 当作实时态的隐式假设；改为 `Device.lastKnownStatus` + 运行时 `status` 分离字段。
4. **持久化时机**：运行时状态变更（如 Offline→Online、进入 Fault）时，由 Runtime 层写回 `Device.LastKnownStatus`（节流写入，避免高频 IO）。
5. **运维字段补全（附带，低成本）**：将 `ReconnectCount` / `FaultCount` 这类希望跨重启保留的计数，纳入可持久化的统计表或在状态写回时一并保存（本期先保证 `LastKnownStatus` 落地，计数持久化作为可选子项）。

### 影响面
- 后端：Device.cs 加字段 + 新增 EF 迁移 + Runtime 层写回逻辑 + 状态接口
- 前端：types.ts Device 接口拆分 `status`(实时) 与 `lastKnownStatus`(持久)；deviceStore / 状态展示组件
- 数据库：一次新增列迁移（带默认值，存量数据安全）

---

## 问题三：ModelVariable.Type 语义混乱（注释"输入/输出/内存"，实际只有 Analog/Digital）

### 现状
- `ModelVariable.cs:29-31` 注释："变量类型（输入/输出/内存等）"，但 `VariableType` 枚举只有 `Analog` / `Digital`。
- 注释与实现矛盾，且"读写方向"概念被 `IsReadOnly` 单独承载，职责重叠不清。

### 风险
开发者按注释理解会误以为有 I/O 方向枚举；`IsReadOnly` 与"类型"两个字段共同表达访问能力，命名易误导。

### 修复方案
**选择 A（推荐，最小改动、语义清晰）**：
1. 修正 `VariableType` 枚举注释与名称，使其准确表达"信号种类"：
   - 重命名为 `SignalKind`（或保留名但改注释为"信号种类：模拟量/数字量"）。
   - 注释明确：本枚举仅表示信号是连续量(模拟)还是离散量(数字)，**不表示访问方向**。
2. 访问方向统一由 `IsReadOnly` 表达（已存在：true=只读，false=可读写），在文档/注释中写明这是唯一访问权限来源。
3. 如未来确需 I/O 区分（输入/输出/内存），**新增独立枚举** `AccessMode { Input, Output, Memory }`，不与 SignalKind 混淆。本期不实现，仅在注释预留。

**选择 B（彻底拆分，改动大）**：立即新增 `AccessMode` 枚举并从 `IsReadOnly` 迁移。
- 不推荐本期做：涉及数据迁移（bool→枚举）与前端同步，超出"一致性风险修复"范围。

### 影响面
- 后端：VariableEnums.cs 改注释/可选重命名；ModelVariable.cs 注释修正
- 前端：types.ts 中 `VariableType` 注释同步；如重命名需同步 `type: VariableType` 引用
- 数据库：无 schema 改动（仅注释，枚举值不变）

---

## 执行顺序建议
1. 先修问题三（纯注释/命名，零 schema 风险，可立即合入）
2. 再修问题一（前端类型补齐，无后端 schema 改动）
3. 最后修问题二（唯一涉及数据库迁移，需谨慎，含回滚预案）

## 回滚预案（问题二）
- 新增列带默认值，迁移可逆（`migration down` 删除列）。
- 写回逻辑加开关：若写回失败不影响实时状态推送，仅持久化降级。

## 待你确认的事项
- [ ] 问题一：是否确认后端 ModbusTcp/BACnet/DNP3 驱动已实现？若未实现，前端是否仍要暴露（标注"暂不支持采集"）？
- [ ] 问题二：是否在 Device 表新增 `LastKnownStatus` 列？还是仅用前端展示层区分（不落库）？
- [ ] 问题三：选 A（改注释，不动字段）还是 B（新增 AccessMode 枚举并迁移）？
