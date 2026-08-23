# P1 修复方案（协议真相源迁回 DataModel）—— 评审稿，待确认后执行

- **状态**：方案阶段，**本文档不改动任何代码**
- **日期**：2026-08-24
- **方向纠偏**：推翻 `docs/device-attribute-refactoring-design.md` 第 9 节拍板结论 #1（"协议真相源在 Device"），改为 **协议真相源在 DataModel**。该旧文档已被用户确认**删除**（不再更新）。
- **前置**：P0 已清零（commit 70cc61c / a757f9b / 2fe9ea5，本地未 push）。P0-2 前端 DeviceType 7 种常量、后端 `IsDriverImplemented` 前置拦截已落地（保留，仅移动位置）。
- **字段命名（已确认）**：协议真相源字段命名为 **`DataModel.Type`**（DeviceType 枚举），与 Device 上被删除的 `Type` 同名但语义不同（Device.Type 是冗余副本，DataModel.Type 是权威）。`DataModel.VendorModel` 保留为纯厂商型号描述，不参与协议选择。
- **数据策略**：用户将删除数据库重来 → **不需要存量回填迁移**，只需 EF 迁移保证新建库 schema 正确。

---

## 0. 为什么要迁回 DataModel（领域建模论证）

用户观点（已采纳）：**数据模型描述的是"某类 PLC/设备的点位表结构"（如"西门子 S7-1200 的 DB1 点位表"），它天然绑定协议。协议应是模型的固有属性，而不是设备实例的属性。**

当前架构（协议在 Device）造成的结构性缺陷：
1. **"新建模型没绑设备就不知道什么协议"** —— 前端 `DataModelView.vue:32-42` 被迫用 `devices.find(d => d.modelId === ...)` 反查协议，未绑设备时回退 `'Virtual'`，造成 dataType 下拉错误。这是补偿代码，病根是协议放错了地方。
2. **变量地址格式校验被迫跳过** —— `ModelVariableAppService.cs:54/101` 注释明确"协议真相源在 Device.Type，此处不再按协议校验地址格式"，导致模型层无法按协议校验点位地址合法性。
3. Device 冗余持有 Type，且与模型协议存在"两份可漂移的真相"。

调整方向后：
- `DataModel.Type`（DeviceType 枚举）成为协议唯一真相源；模型创建时即定协议。
- `Device` 删除 `Type`，通过 `ModelId → Model.Type` 推导驱动协议（设备只能绑一个模型，协议天然唯一）。
- 前端创建设备的协议框改为**只读、自动从所选模型带出**（用户确认）。
- 变量地址校验本轮**跳过**（用户确认留 P2），`ModelVariableAppService` 维持现状。

---

## 1. 后端改动清单

### 1.1 实体层
- `Server/ScadaServer.Domain/Entities/DataModel.cs`
  - 新增 `public DeviceType Type { get; set; }`（协议真相源）。
  - 保留 `VendorModel`（厂商/型号描述，如 "Siemens S7-1500"），仅作展示，与协议解耦。
- `Server/ScadaServer.Domain/Entities/Device.cs`
  - **删除** `public DeviceType Type { get; set; }`（Line 48）。
  - 保留 `Model` 导航属性（已存在 Line 43），驱动推导依赖它。

### 1.2 DTO 层
- `Server/ScadaServer.Application/DTOs/DataModelDto.cs`
  - 新增 `public DeviceType Type { get; set; }`；MapToDto 映射 `Type = entity.Type`。
- `Server/ScadaServer.Application/DTOs/CreateDataModelDto.cs`（待确认文件存在；若不存在则在 DataModelDto 上复用/新增）
  - 新增 `public DeviceType Type { get; set; }`（创建模型必填协议）。
- `Server/ScadaServer.Application/DTOs/CreateDeviceDto.cs`
  - **删除** `Type` 字段（Line 25-29）及其 `[Required]`。
- `Server/ScadaServer.Application/DTOs/DeviceDto.cs`
  - **删除** `Type` 映射（`DeviceAppService` 中 Line 59/80 的 `Type = entity.Type` 移除）；前端 Device 的 `type` 改为从 `modelId` 反查或用独立只读字段——见前端章节。

### 1.3 应用服务层
- `Server/ScadaServer.Application/Services/DataModelAppService.cs`
  - `CreateAsync`：接收 `dto.Type`，写入 `entity.Type`。
  - `UpdateAsync`：同步 `entity.Type = dto.Type`。
  - `MapToDto`：输出 `Type`。
- `Server/ScadaServer.Application/Services/DeviceAppService.cs`
  - `CreateAsync`：
    - 已有 `model = await _modelRepository.GetByIdAsync(dto.ModelId)`（Line 199），直接取 `model.Type`。
    - 前置拦截改为基于 `model.Type.IsDriverImplemented()`（Line 207 → 用模型协议判断）。
    - `ValidateConfigJson(model.Type, dto.ConfigJson)`（Line 213，去掉"协议在 Device"注释）。
    - 实体构造去掉 `Type = dto.Type`（Line 237）。
  - `UpdateAsync`：
    - 协议不再可改（设备协议=模型协议，模型改协议走模型更新；允许改 ModelId，协议随模型推导）。去掉 Line 301 `entity.Type = dto.Type`、Line 292 的 `ValidateConfigJson(dto.Type, ...)` → 改为取 `model.Type` 校验。
  - `GetByIdAsync` / `GetListAsync`：移除 `Type = entity.Type`。
- `Server/ScadaServer.Application/Services/ModelVariableAppService.cs`
  - **本轮跳过**（用户确认留 P2），维持现状。

### 1.4 运行时层
- `Server/ScadaServer.Runtime/RuntimeManager.cs`
  - Line 121 `CreateDriver(device.Type)` → `CreateDriver(model.Type)`（model 已在 Line 124 加载）。
  - Line 130/151/156 日志 `device.Type` → `model.Type`。

### 1.5 EF 迁移
- 新增迁移 `AddTypeToDataModel_RemoveDeviceType`（仅新建库生效，符合既定策略）：
  - `DataModels` 表 ADD COLUMN `Type`（`int`，非空）。
  - `Devices` 表 DROP COLUMN `Type`。
- 因用户删库重来，迁移只需保证 schema 正确，无需历史数据回填脚本。

---

## 2. 前端改动清单

### 2.1 类型定义
- `Client/src/types.ts`
  - `DataModel` 接口（Line 141-147）：`type: DeviceType` **转正为真实协议字段**（不再是反查兜底）；保留 `vendorModel?` 可选展示字段（对接后端 VendorModel）。
  - `Device` 接口（Line 149-175）：`type: DeviceType` 改为可选只读派生，建议改用 `modelType?: DeviceType`（从 modelId 反查模型 type），或直接移除 device.type、界面统一从 `dataModels` 查。

### 2.2 模型创建/编辑（DataModelView.vue）
- `modelType` 表单字段（Line 48）**保留但语义转正**：不再是"被丢弃的前端残留"，而是**真实提交到后端 `type` 字段**。
- 新建模型提交体（`createDataModelOnBackend` / `modelApi.ts`）带上 `type`。
- `currentModelProtocol` / `protocolOf`（Line 32-42）反查逻辑**简化**：不再回退 `'Virtual'`，直接读 `currentModel.value.type`。

### 2.3 模型同步（modelApi.ts）
- `createDataModelOnBackend`（Line 18-19）：`type: 'Virtual' as any` 兜底 → 改为用后端返回的 `type`（后端将返回真实协议）。
- `fetchDataModelsFromBackend`（Line 44-46）：同理，用 `m.type` 而非硬编码 `'Virtual'`。

### 2.4 设备创建/编辑（DeviceManagementView.vue + deviceService.ts）
- `devType`（Line 52）改为**只读**：`onModelChange`（Line 191-210）继续从所选模型带出，但表单禁用该选择控件。
- `openNewDeviceModal`（Line 169）：`devType.value = initialModel?.type || 'OPCUA'`。
- `buildConfigJson(type)`（Line 249）：入参改为接收模型 type（从 `devModel` 查表），不再用独立 devType 覆盖。
- `deviceService.ts` 提交体（Line 61/102 `type: deviceData.type`）：**移除 type 字段**（后端不再接收），协议由后端从 modelId 推导。
- `DeviceDto` 后端不再返回 Type → 前端 `device.type` 改为从 `dataModels.find(m=>m.id===device.modelId)?.type` 派生显示（统一函数，避免散落）。

### 2.5 其他引用点（需核查）
- `LiveDataView.vue` / `DeviceManagementView.vue` 设备类型筛选/徽标：由 `device.type` 改为 `device.modelType`（派生）。
- `ScadaTopologyView.vue` 等若有用 `device.type` 处一并替换。

---

## 3. 改造前后对比

| 维度 | Before（协议在 Device） | After（协议在 DataModel） |
|------|------------------------|--------------------------|
| 模型无设备时的协议 | 回退 Virtual（错误） | 模型自带 Type，明确 |
| Device 字段 | 持有 Type（冗余） | 无 Type，从 Model 推导 |
| 前端创建设备协议框 | 可选手动选（易与模型冲突） | **只读，自动从模型带出** |
| 变量地址校验 | 被迫跳过 | 本轮仍跳过（留 P2） |
| 驱动选择 | `CreateDriver(device.Type)` | `CreateDriver(model.Type)` |

---

## 4. 执行顺序（分批提交，每步 build 验证，默认不 push）

1. **后端 schema + 实体 + DTO**（含 EF 迁移）→ 单独 commit。验证：`dotnet build` 0 错误。
2. **后端服务层**（DeviceAppService / DataModelAppService / RuntimeManager）→ 单独 commit。验证：build + 启动初始化日志。
3. **前端类型 + 模型层**（types.ts / DataModelView / modelApi）→ 单独 commit。验证：`tsc --noEmit`。
4. **前端设备层**（DeviceManagementView / deviceService / 筛选徽标）→ 单独 commit。验证：`tsc --noEmit` + 手动联调（删库后建模型→建设备→协议只读带出）。

---

## 5. 已确认的执行细节（2026-08-24，已全部拍板）

- [x] **DataModel 协议字段命名**：使用 **`Type`**（与 Device 上被删除的 Type 同名，语义不同：DataModel.Type 是协议权威，Device.Type 是已删的冗余副本）。
- [x] **`VendorModel`（厂商型号描述）去留**：**保留**为纯展示字段，与协议解耦。
- [x] **设备更新时能否改绑定模型（ModelId）**：**允许**，协议随模型自动推导（UpdateAsync 维持允许改 ModelId）。
- [x] **变量地址校验恢复**（ModelVariableAppService）：**本轮跳过**，留 P2。
- [x] **`docs/device-attribute-refactoring-design.md`**：**直接删除**（不再更新，用户确认）。

---

## 6. 风险提示

- 前端 `device.type` 散落引用较多（DeviceManagementView / LiveDataView / 可能 ScadaTopologyView），替换时需全局 grep 确认无遗漏，否则 TS 编译报错或运行时 undefined。
- 后端删除 Device.Type 后，任何仍引用 `entity.Type` 的代码（含仓库层、映射器）必须同步清理——已 grep 确认消费点为 DeviceAppService（4 处）+ RuntimeManager（4 处），无其他。
- 因删库重来，无需担心存量数据兼容；但前端 mock 模式（isSimulationActive）的 `createDeviceAndSync` 兜底对象仍带 `type` 字段，需同步改为从模型派生，否则模拟模式下设备协议显示异常。
