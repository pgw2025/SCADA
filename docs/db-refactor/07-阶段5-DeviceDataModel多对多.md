# 阶段 5：DeviceDataModel 多对多绑定（4-5 天，中高风险）

> 目标：新增目标设计的 `DeviceDataModels` 中间表，实现设备与数据模型多对多绑定（支持版本/主模型）。**保守策略：运行时变量解析仍以"主模型"为唯一生效集合**——多模型变量合并（跨模型同名 Key 冲突消解）明确不在本阶段。
> 前置：阶段 3、阶段 4 完成。

---

## 设计要点

1. **反范式保留 `Device.ModelId`**：作为"主模型"快捷列与现有代码的兼容锚点，`DeviceDataModels` 中 `IsPrimary=true` 的行必须与 `Device.ModelId` 一致（双向同步单点维护）。是否最终移除 Device.ModelId 由阶段 6 评估。
2. 一台设备**至多一条 IsPrimary=true**（应用层 + 数据库部分唯一索引保障；MySQL 8 可用函数索引 `((IsPrimary = 1 AND IsPrimary IS NOT NULL))`，或应用层校验 + 事务，MVP 用应用层校验）。
3. (DeviceId, DataModelId) 唯一索引防重复绑定。
4. 运行时 Include 链：加载设备主模型仍走 `Device.Model`（零运行时改动）；非主模型绑定仅供管理界面与未来扩展。
5. `Version` 列取绑定时刻模型的 `DataModel.Version` 快照（记录"当时用的版本"）。

## 实体设计：`DeviceDataModels`（新表）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | int 自增 | 主键 |
| DeviceId | int FK→Devices (Cascade) | 设备 |
| DataModelId | int FK→DataModels (Restrict) | 模型 |
| Version | string(20) | 绑定版本快照，默认取模型当前 Version |
| IsPrimary | bool, 默认 false | 是否主模型 |
| IsEnabled | bool, 默认 true | 绑定启用 |
| CreatedAt / UpdatedAt | DateTime | UTC |

索引：`(DeviceId, DataModelId)` 唯一；`ix_devicedatamodels_deviceid`。

---

## 步骤 5.1：实体 + 迁移 + 回填

**任务**：
1. 新增 `ScadaServer.Domain/Entities/DeviceDataModel.cs`（字段如上；导航 Device/DataModel）。
2. ScadaDbContext：DbSet + FK + 索引。
3. 迁移 `AddDeviceDataModelBindings`（新表）+ 回填迁移 `BackfillDeviceDataModels`：
   ```sql
   INSERT INTO DeviceDataModels (DeviceId, DataModelId, Version, IsPrimary, IsEnabled, CreatedAt, UpdatedAt)
   SELECT d.Id, d.ModelId, COALESCE(m.Version, '1.0'), 1, 1, d.CreatedAt, d.UpdatedAt
   FROM Devices d JOIN DataModels m ON d.ModelId = m.Id;
   ```
   （若阶段 4 未完成，Version 用 '1.0' 常量——按实际执行顺序调整。）
4. 回填校验 SQL：绑定行数 = 设备数；每设备 IsPrimary 恰好 1 条；无 (DeviceId,DataModelId) 重复。

**验收**：回填统计通过；运行时零影响（Include 链未改）。

## 步骤 5.2：绑定管理 API（后端）

**任务**：
1. 新增 `DeviceDataModelAppService`：
   - `GetByDeviceAsync(deviceId)`：设备全部绑定（含模型摘要 Code/Name/Version/变量数）；
   - `BindAsync(deviceId, dataModelId, isPrimary)`：校验——模型存在且 IsPublished；同模型不重复绑定；设为主模型时事务内先把旧主模型降级并同步 `Device.ModelId`（**唯一双写点**）；
   - `UnbindAsync`：主模型不可解绑（先切换主模型）；解绑时校验该模型下 DeviceVariable 引用并按策略拒绝/级联删除（MVP：拒绝并提示先清理该模型下的设备变量）；
   - `SetPrimaryAsync`：切换主模型 = 同步 Device.ModelId（事务）。
2. **修改 [DeviceAppService.cs](../../Server/ScadaServer.Application/Services/DeviceAppService.cs)**：
   - 创建设备：写 Device.ModelId 的同时插入一条 IsPrimary 绑定（双写）；
   - 更换设备主模型（现有接口）：同步更新绑定表（双写）；
   - 删除设备：绑定行随 FK Cascade 删除，无需额外逻辑。
3. 新增 `DeviceDataModelController`（`/api/devices/{deviceId}/data-models` RESTful 子资源路由）。
4. 修改设备详情 DTO：返回 `models: [{modelId, code, version, isPrimary}]` 列表 + 保留旧 `modelId` 字段（= 主模型）。

**验收**：绑定/解绑/切主全流程 API 手测；双写一致性 SQL 抽查（`Device.ModelId` 与 `IsPrimary=1` 行严格一致）；现有前端不改代码可正常运行（旧字段仍在）。

## 步骤 5.3：运行时主模型加载路径确认（小改或零改）

**任务**：
1. 评估 [RuntimeManager.cs](../../Server/ScadaServer.Runtime/RuntimeManager.cs) 的 Include：当前已 `Include(Model)`——**保持不变即为主模型路径**。
2. （可选小改）启动日志补充"设备 X 绑定 N 个模型，主模型 Y"信息行，便于运维确认。
3. 明确注释：多模型变量合并（非主模型变量的采集）为后续版本特性，本阶段运行时只认主模型。

**验收**：冒烟清单全绿；启动日志出现绑定信息。

## 步骤 5.4：前端绑定 UI

**任务**：
1. 新增 `Client/src/api/deviceDataModelApi.ts`。
2. [DeviceManagementView.vue](../../Client/src/components/DeviceManagementView.vue)：
   - 设备详情/编辑增加"数据模型"分区：主模型下拉（= 现有 ModelId 选择的增强入口）+ 附加模型多选列表（仅管理，提示"附加模型暂不参与采集"）；
   - 绑定操作（绑定/解绑/设为主模型）。
3. [DeviceVariableView.vue](../../Client/src/components/DeviceVariableView.vue)：变量列表顶部显示当前主模型 Code/Version（只读展示）。

**验收**：绑定界面全流程可用；旧设备管理路径（直接选模型）行为不变；`npm run build` 通过。

## 步骤 5.5：收尾

**任务**：双写一致性巡检 SQL 留档（供阶段 7 回归复用）；键值快照比对；合并分支，tag `db-refactor-phase5`。

---

## 回滚方案

- 应用回滚：旧代码读写 Device.ModelId（双写保证数据正确），绑定表闲置无害；
- 若需结构回滚：先删 DeviceDataModels 数据再回退迁移。

## 风险清单

| 风险 | 等级 | 对策 |
|---|---|---|
| Device.ModelId 与 IsPrimary 双写失同步 → 主模型错乱 | 高 | 双写收敛在 DeviceAppService + DeviceDataModelAppService 事务单点；步骤 5.2/5.5 一致性 SQL 抽查；阶段 7 纳入回归 |
| 解绑导致 DeviceVariable 悬挂（引用了被解绑模型的 ModelVariable） | 中 | 解绑前校验并拒绝（提示清理）；提供按模型筛选设备变量的辅助查询 |
| 误以为附加模型会参与采集 | 低 | UI 明示 + 运行时注释 + 本文档声明 |
| 主模型切换后变量集变化未热重载 | 中 | 切主模型复用现有设备热重载接口（更新 ModelId 已有重载链路，行为不变） |
