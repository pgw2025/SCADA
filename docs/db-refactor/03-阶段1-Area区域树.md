# 阶段 1：Area 区域树（2-3 天，低风险纯加法）

> 目标：把现有平级 `Areas` 表升级为目标设计的树形区域（ParentId/AreaType/Sort/Enabled/Code 唯一），并提供树形 API 与前端树组件。**不影响任何运行时采集链路。**
> 前置：阶段 0 完成。

***

## 实施状态（2026-09-03）

- [x] 1.1 实体与枚举：`AreaTypeEnum` + `Area` 实体扩展（ParentId/AreaType/Sort/IsEnabled/CreatedAt/UpdatedAt + Parent/Children 导航）

- [x] 1.2 DbContext 配置 + 迁移 `AddAreaTreeFields`（自引用 FK Restrict、ix\_areas\_parentid 索引、列类型收敛）

- [x] 1.3 迁移 `AddAreaCodeUniqueIndex`（先清洗空串为 NULL，再建 ix\_areas\_code 唯一索引）

- [x] 1.4 后端树形 API：`GET /api/Area/tree`、`GET /api/Area/{id}/device-ids`；Create/Update 支持父区域/类型/编码/排序/启用；更新防环校验；删除校验子区域与设备

- [x] 1.5 前端：`AreaTree.vue` 树组件；设备列表树形区域筛选（含"包含子区域"开关，默认全部=原行为）；区域管理树形表格（增删改 + 选父调整层级）；设备表单区域下拉树形化

- [x] 1.6 收尾：后端 `dotnet build` 通过；迁移已应用到开发库（存量区域回填 AreaType=4/ParentId=NULL/IsEnabled=1）；树接口冒烟通过（创建/重复编码拒绝/更新/删除）；前端 `vue-tsc` 与 `vite build` 通过（遗留 4 处 widgets 既有类型错误与本阶段无关）

***

## 设计要点

1. 现有区域数据全部保持 Id/Name/Code 不变，回填为**根区域同级列表**（ParentId=NULL）——不臆造层级，由用户后续自行拖拽成树。
2. `Area.Code` 现被用于**设备 Key 自动生成前缀**，加唯一索引时必须兼容存量 NULL/空值（MySQL 唯一索引允许多个 NULL，但需先清洗空字符串为 NULL）。
3. `AreaType` 用 int 枚举：Factory=1, Workshop=2, ProductionLine=3, Area=4, Warehouse=5（默认 Area）。
4. 设备列表按区域过滤仍用 `WHERE AreaId = ?`；"区域下含子区域设备"查询通过**内存树展开 + IN 查询**实现（区域量级小，无需闭包表），代码放应用服务层。

***

## 步骤 1.1：实体与枚举

**任务**：

1. 新增枚举 `ScadaServer.Domain/Enums/AreaTypeEnum.cs`（Factory/Workshop/ProductionLine/Area/Warehouse）。
2. 修改 [Area.cs](../../Server/ScadaServer.Domain/Entities/Area.cs)：新增 `ParentId`(int?)、`AreaType`(AreaTypeEnum, 默认 Area)、`Sort`(int, 默认 0)、`IsEnabled`(bool, 默认 true)、`CreatedAt`/`UpdatedAt`(DateTime, 默认 UtcNow)；新增导航 `List<Area> Children` 与 `Area? Parent`。保留现有 Name/Code/Description 原样。

**验收**：`dotnet build` 通过；实体注释说明字段语义（Code 用于设备编号前缀，不可随意变更）。

## 步骤 1.2：EF 结构迁移（仅加列）

**任务**：

1. `ScadaDbContext` 新增 `DbSet<Area>`（如已隐式配置则补关系）：`ParentId` 自引用 FK（DeleteBehavior.Restrict，防止误删有子区域的区域）、索引 `ix_areas_parentid`。
2. `dotnet ef migrations add AddAreaTreeFields`：全部新列可空或带默认值，**不加唯一索引**（下一步做）。
3. 手工评审生成的迁移 SQL。

**验收**：开发库 `database update` 成功；存量区域行 ParentId=NULL、AreaType=4、Sort=0、IsEnabled=1。

## 步骤 1.3：Code 清洗 + 唯一索引迁移

**任务**：

1. `dotnet ef migrations add AddAreaCodeUniqueIndex`，迁移内先执行清洗 SQL：

   ```sql
   UPDATE Areas SET Code = NULL WHERE Code = '';
   ```

   再创建唯一索引（MySQL 下用 `(Code)` 唯一索引即可，NULL 互不冲突）。
2. 同步在 `ScadaDbContext` 配置 `HasIndex(Code).IsUnique()`（与迁移一致）。

**验收**：重复插入同 Code 被 DB 拒绝；NULL Code 可多条共存。

## 步骤 1.4：树形 API（后端）

**任务**：

1. [AreaRepository.cs](../../Server/ScadaServer.Infrastructure/Repositories/AreaRepository.cs)：新增 `GetAllAsync()`（含子区域导航或全量取回内存组树）、删除前校验"无子区域且无设备引用"。
2. 新增 `ScadaServer.Application/Services/AreaAppService.cs`（若已有 Area 服务则扩展）：

   - `GetTreeAsync()`：返回树形 DTO（Id/ParentId/Code/Name/AreaType/Sort/IsEnabled/Children/DeviceCount）；

   - `CreateAsync/UpdateAsync/DeleteAsync`：删除校验（有子区域或挂设备则拒绝，返回明确错误码）；层级防环校验（父不能是自己或后代）；

   - `GetDeviceIdsInSubtreeAsync(int areaId)`：供按区域（含子区域）过滤设备。
3. 扩展 [AreaController.cs](../../Server/ScadaServer.WebApi/Controllers/AreaController.cs)：`GET /api/areas/tree`、现有 CRUD 保持兼容（旧响应字段不删，新增字段可选输出）。

**验收**：Postman/浏览器调用树接口返回正确层级；删除被引用区域返回业务错误而非 500。

## 步骤 1.5：前端区域树组件

**任务**：

1. 新增 `Client/src/components/AreaTree.vue`（或扩展现有区域选择器）：懒加载/全量树渲染、按 AreaType 显示图标、支持选择回调。
2. [DeviceManagementView.vue](../../Client/src/components/DeviceManagementView.vue) 设备列表的区域筛选改为树选择（保留"仅当前区域/含子区域"开关，默认仅当前区域，**不改变现有筛选行为**）。
3. 设备创建表单的区域下拉沿用现有单选 AreaId（树选节点即写入其 Id，行为不变）。
4. 新增/扩展区域管理 UI：树形增删改、拖拽或"选父区域"方式调整层级（MVP 可只做选父下拉）。

**验收**：区域树增删改正常；设备按区域（含子区域）过滤正确；现有设备管理功能回归无异常（跑阶段 0 冒烟清单）。

## 步骤 1.6：收尾

**任务**：

1. 检查全库 `rg "Areas"` 引用点（如设备 Key 生成器使用 Area.Code 的逻辑）确认行为不变；
2. 重跑阶段 0 冒烟清单；
3. 合并分支，打 tag `db-refactor-phase1`。

***

## 回滚方案

- 结构迁移均为加列：直接回滚应用版本即可，新列闲置无害；

- 若需回滚库结构：`Update-Database -Migration <前一迁移>`（开发环境）；生产环境优先"应用回滚 + 列闲置"策略。

## 风险与注意

| 风险                                | 对策                                            |
| --------------------------------- | --------------------------------------------- |
| 设备 Key 生成依赖 Area.Code，区域 Code 被误改 | 更新接口对 Code 变更做警告提示（提示影响后续新生成设备编号，不影响既有设备 Key） |
| 树形数据量增长后全量加载                      | 区域量级天然有限（工厂/车间级），全量内存组树足够；不为伪需求做闭包表           |

