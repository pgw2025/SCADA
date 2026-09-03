# 阶段 3：DeviceConnection 连接抽取（5-7 天，**全方案最高风险阶段**）

> 目标：把"设备怎么连接"（现散落在 `Device.JsonConfig` + `DataModel.ProtocolId`）抽取为 `Controller + DeviceConnection` 两个实体，运行时改为从新表读取连接参数（**双读兼容**：新表缺失时回退 JsonConfig）。
> 前置：阶段 2（Controller 实体已存在）。
> 核心原则：**驱动层（S7Driver/OpcUaDriver/VirtualDriver）与 ProtocolDriverFactory 零修改**。

---

## 设计决策（本阶段特有）

| # | 决策 | 理由 |
|---|---|---|
| P3-A | **过渡期每台设备独立一个 Controller + 一个 DeviceConnection**（1 设备 = 1 连接，与现状行为 100% 等价） | 保证回填后采集行为不变；"多设备共享控制器/连接"是管理界面上的后续手工合并，不属本阶段 |
| P3-B | **DeviceConnection.ConfigJson 存放驱动完整配置**（即现 JsonConfig 原文，含 IP/端口/端点），`Host`/`Port` 为额外提取的冗余列（管理/检索用） | 避免拆装 JSON 的有损往返；运行时兼容层可以做到"逐字节等价"，极大降低回归风险 |
| P3-C | 驱动面向的 JSON 结构（S7Config/OpcUaConfig/VirtualConfig，见 [DeviceConfigDto.cs](../../Server/ScadaServer.Application/DTOs/DeviceConfigDto.cs)）**保持不变** | S7Driver/OpcUaDriver/VirtualDriver 反序列化 `device.ConfigJson`，只要 IRuntimeDevice.ConfigJson 继续吐出同样 JSON，驱动无感知 |
| P3-D | 新增过渡列 `Device.ConnectionId`（设备默认连接）+ `Device.ControllerId`；`DeviceVariable.ConnectionId` 留到阶段 4（变量级可选覆盖） | 先设备级后变量级，每步可独立验证 |
| P3-E | `DataModel.ProtocolId` **保留但语义降级**为"新建设备时的默认协议"（不再参与运行时派发） | 消除大改 DataModel 的风险；彻底移除放阶段 6 评估 |

### 运行时兼容层核心机制

```
现状:  DeviceRuntime.ConfigJson => Device.JsonConfig ?? "{}"

阶段3: DeviceRuntime.ConfigJson =>
    (Device.Connection != null)
        ? Device.Connection.ConfigJson          // 新路径：连接表（回填后 = 原 JsonConfig 原文）
        : (Device.JsonConfig ?? "{}")           // 旧路径回退：尚未回填/新建未迁移数据
```

驱动派发键同样改为：`Device.Connection?.Protocol?.DriverKey ?? Device.Model?.Protocol?.DriverKey`（回退链）。

---

## 实体设计

### `DeviceConnections`（新表）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | int 自增 | 主键 |
| ControllerId | int FK→Controllers (Restrict) | 所属控制器 |
| Name | string(100) | 连接名称 |
| ProtocolId | int FK→Protocols (Restrict) | 协议（S7/OPCUA/Virtual...） |
| Host | string(100)? | 提取的 IP/主机（S7=IpAddress；OPC UA=端点 URL 主机；Virtual=NULL） |
| Port | int? | 提取端口（S7=Port；OPC UA=端点端口；Virtual=NULL） |
| ConfigJson | longtext? | **驱动完整配置原文**（P3-B） |
| TimeoutMs | int, 默认 5000 | IO 超时（回填时取 S7 IoTimeoutMs，无则默认） |
| ReconnectIntervalMs | int, 默认 5000 | 重连周期（回填默认，与现运行时行为一致） |
| IsEnabled | bool, 默认 true | 启用 |
| CreatedAt / UpdatedAt | DateTime | UTC |

索引：`ix_deviceconnections_controllerid`。

### `Devices` 表新增列（均可空/带默认）

- `ControllerId` int? FK→Controllers (Restrict)
- `ConnectionId` int? FK→DeviceConnections (Restrict)

---

## 步骤 3.1：实体 + 迁移 1（纯结构，加列不加约束）

**任务**：
1. 新增 `ScadaServer.Domain/Entities/DeviceConnection.cs`（字段如上；导航 `Controller`、`Protocol`）。
2. [Device.cs](../../Server/ScadaServer.Domain/Entities/Device.cs)：新增 `ControllerId`/`ConnectionId` + 导航 `Controller`/`Connection`（**不动 JsonConfig/Version 等现有字段**）。
3. ScadaDbContext：新增 `DbSet<DeviceConnection>`、两条 FK、索引；Device 的两条新 FK。
4. `dotnet ef migrations add AddDeviceConnectionEntity`（全部可空列 + 新表；**不含回填**）。

**验收**：`database update` 成功；现有设备行 ControllerId/ConnectionId 均为 NULL；采集一切照旧（运行时未改动）。

## 步骤 3.2：回填迁移 2（数据迁移，本阶段核心）

**任务**：`dotnet ef migrations add BackfillControllerAndConnection`，回填逻辑用 **C# 代码在迁移中执行**（EF 迁移支持 `migrationBuilder.Sql`，但 JSON 解析用 C# 更稳：可在迁移类中直接操作 DbContext——采用"迁移内注入 DbContext 的自定义迁移"模式，或在迁移 Sql 中使用 MySQL `JSON_EXTRACT`；**推荐前者**，项目内已有 DatabaseInitializationStatus/HostedService 先例，也可做成一次性命令行回填：新增 `ScadaServer.WebApi` 下的回填 HostedService，启动时检测未回填则执行，执行完打标）：

回填算法（伪代码）：
```
foreach device in Devices:
    protocolId = device.Model.ProtocolId
    json = device.JsonConfig ?? "{}"
    (host, port) = 按 protocolId 解析:
        S7:     (json.IpAddress, json.Port 默认102)
        OPCUA:  (URL 主机, URL 端口 默认4840)
        Virtual:(NULL, NULL)
    controller = new Controller {
        Code = "PLC" + device.Id,          // 保证唯一
        Name = device.Name + " 控制器",
        ProtocolId, Manufacturer = device.Model.Vendor,
        Model = device.Model.ModelName, IsEnabled = true }
    connection = new DeviceConnection {
        ControllerId = controller.Id, Name = device.Name + " 连接",
        ProtocolId, Host, Port,
        ConfigJson = json,                  // 原文整体存入（P3-B）
        TimeoutMs = json.IoTimeoutMs ?? 5000,
        IsEnabled = true }
    device.ControllerId = controller.Id
    device.ConnectionId = connection.Id
    // JsonConfig 不删不改（兼容回退层要用）
```

要求：
1. 回填**幂等**：仅处理 `ConnectionId IS NULL` 的设备；重复执行不产生重复行。
2. 空设备（无 JsonConfig）：Controller/Connection 仍建立，ConfigJson="{}"，Host/Port=NULL。
3. 回填完成后输出统计日志（设备数、成功数、跳过数）。

**验收**：
- 每台设备恰好 1 Controller + 1 Connection，`Connection.ConfigJson` 与 `Device.JsonConfig` **逐字节一致**（用 SQL 抽样比对）；
- `Device.ConnectionId` 全部非 NULL；
- InfluxDB tag 无任何变化（Device.Key 未动）；
- 运行时行为照旧（尚未切换读取路径）。

## 步骤 3.3：运行时切换读取路径（双读兼容层）

**任务**：
1. [RuntimeManager.cs](../../Server/ScadaServer.Runtime/RuntimeManager.cs) `InitializeAsync` / `LoadDeviceGraphByIdAsync` 的 Include 链扩展：
   ```
   Device
     .Include(Controller)                       // 新
     .Include(Connection).ThenInclude(Protocol) // 新
     .Include(Model).ThenInclude(Protocol)      // 保留（回退用）
     .Include(DeviceVariables).ThenInclude(ModelVariable)
   ```
2. [DeviceRuntime.cs](../../Server/ScadaServer.Runtime/Devices/DeviceRuntime.cs) `ConfigJson` 属性改为（P3 机制）：
   ```csharp
   public string ConfigJson =>
       Device.Connection?.ConfigJson
       ?? Device.JsonConfig
       ?? "{}";
   ```
   [IRuntimeDevice.cs](../../Server/ScadaServer.Domain/Interfaces/IRuntimeDevice.cs) 注释同步说明双读语义。
3. 驱动键解析（RuntimeManager 中创建驱动处）：优先 `Device.Connection.Protocol.DriverKey`，回退 `Device.Model.Protocol.DriverKey`。
4. **S7Driver / OpcUaDriver / VirtualDriver / ProtocolDriverFactory：零修改**（P3-C）。

**验收**（重点回归）：
- S7 真机/虚拟设备连接成功，实时值、写值、报警、历史全部正常；
- 日志中连接参数（IP/rack/slot/端点）与重构前完全一致；
- 手工把某设备 `ConnectionId` 置 NULL 模拟未回填 → 仍能经 JsonConfig 回退运行。

## 步骤 3.4：写路径切换（应用服务）

**任务**：[DeviceAppService.cs](../../Server/ScadaServer.Application/Services/DeviceAppService.cs)（`rg "JsonConfig"` 确认全部 5 处消费点）：
1. **创建设备**：若请求带协议连接参数（现 DTO 结构），除写 Device.JsonConfig 外，**同步创建 Controller+Connection 并回填 Device 两个新列**（双写）。
2. **更新连接参数**：同步更新 Connection.ConfigJson/Host/Port（**ConfigJson 仍保存完整 JSON 原文**，与设备提交的 JSON 一致），并保持 Device.JsonConfig 双写（兼容期）。
3. **删除设备**：级联清理其独占的 Controller+Connection（仅当无其他设备引用）。
4. 设备协议配置版本 `Device.Version` 自增逻辑保留，同时更新 Connection.UpdatedAt。
5. [DeviceRepository.cs](../../Server/ScadaServer.Infrastructure/Repositories/DeviceRepository.cs) 查询投影补充 Controller/Connection 信息（供 API 返回）。

**验收**：用**现有前端**（未改版）完成设备创建→采集→改 IP→热重载→删除全流程（双写保证新旧路径数据一致）；改 IP 后连接按新参数重连成功。

## 步骤 3.5：连接/控制器管理 API 完善

**任务**：
1. 新增 `DeviceConnectionAppService` + `DeviceConnectionController`（`/api/device-connections`）：按控制器查连接、CRUD、连接测试（复用现有连接测试逻辑如有）。
2. 扩展阶段 2 的 ControllerAppService：返回连接数量；删除校验增加"存在连接或设备引用时拒绝"。
3. 扩展 [DeviceController.cs](../../Server/ScadaServer.WebApi/Controllers/DeviceController.cs) 的设备详情 DTO：返回 controllerId/connectionId/连接摘要（host/port/protocol），**旧字段（jsonConfig/protocolId）保留只读输出**（兼容期前端不炸）。

**验收**：Swagger 验证；现有前端设备管理页在**不改代码**的情况下正常显示与操作。

## 步骤 3.6：前端设备表单改造（第二步前端改动）

**任务**：
1. [deviceApi.ts](../../Client/src/api/deviceApi.ts)：设备 DTO 增加 controllerId/connectionId 及连接摘要字段（旧字段保留）。
2. [DeviceManagementView.vue](../../Client/src/components/DeviceManagementView.vue) 设备编辑表单：
   - 方案（推荐渐进）：保留原"协议连接参数"分区作为"快速模式"（提交时后端仍自动维护连接）；新增"高级模式"：下拉选择控制器 + 连接（或"新建独立连接"）；
   - 协议显示来源从 DataModel 切换为设备连接摘要（兼容期两者都显示）。
3. 连接变更后触发现有设备热重载接口（不变）。

**验收**：两种模式创建的设备都能正常采集；高级模式选择共享连接（手工把两台设备 ConnectionId 指向同一连接）→ 两设备可经同一连接参数运行（行为等价验证）。

## 步骤 3.7：观察期与收尾

**任务**：
1. 开发/测试环境运行**不少于 2-3 天**稳定观察（含断电重连、写值、报警）。
2. 重跑阶段 0 冒烟清单 + 阶段 0.2 的键值快照比对（Devices.Key/ModelVariables.Key 必须零变化）。
3. 合并分支，tag `db-refactor-phase3`。
4. **JsonConfig 兼容回退层的删除推迟到阶段 6**（观察期后）。

---

## 回滚方案

| 层 | 回滚动作 |
|---|---|
| 应用 | 回滚到上一版本即可：新列/新表对旧代码不可见（旧代码读写 JsonConfig，双写保证数据仍在） |
| 数据 | 无需回滚（JsonConfig 全程保留且与 Connection 同步） |
| 结构 | 极端情况 `Update-Database -Migration <phase3 前>`（须先手动清空 Controllers/DeviceConnections 数据并解除引用） |

## 风险清单

| 风险 | 等级 | 对策 |
|---|---|---|
| 回填后连接参数与原 JsonConfig 不一致 → 全线连接失败 | 高 | P3-B 原文存储 + 步骤 3.2 逐字节比对验收 + 3.3 回退层 |
| RuntimeManager Include 链遗漏 → 设备注册失败 | 中 | Include 扩展是纯增量；保留 Model.Protocol 回退 |
| 双写不一致（改了 JsonConfig 忘改 Connection） | 中 | 双写集中在 DeviceAppService 单点；步骤 3.4 验收覆盖"改 IP"路径 |
| 设备删除遗留孤儿 Controller/Connection | 低 | 删除逻辑单点实现 + 唯一性统计 SQL 抽查 |
| 前端新旧 DTO 并存期字段冲突 | 低 | 旧字段只读保留，一个版本周期后再删 |
