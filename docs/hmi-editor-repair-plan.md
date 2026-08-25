# 组态设计（HMI Editor）全量修复方案

- **状态**：方案稿，待用户确认后执行（**本文档不改动任何代码**）
- **日期**：2026-08-25
- **输入**：本轮「组态设计前后端框架分析」结论（含两个未记录于 p0/p1-fix-plan 的新 P0）
- **总原则**：改动最小化、分阶段可独立交付、每阶段有明确验收标准、可单独回滚
- **数据库策略**：延续 P1 方案的惯例——**可删库重建，不做存量数据回填迁移**，EF 迁移只需保证新库 schema 正确

---

## 阶段总览与依赖关系

| 阶段 | 主题 | 规模 | 依赖 |
|------|------|------|------|
| 0 | 快速止血（独立前端小修） | S | 无，随时可做 |
| 1 | 后端组态 API 补强 + 一次性 schema 到位 | M | 无 |
| 2 | 前端组态持久化打通 | L | 阶段 1 |
| 3 | 变量绑定模型升级（deviceId + variableKey） | M | 阶段 1（schema 已在阶段 1 预埋） |
| 4 | 控制写入链路打通（Hub 上行 + REST 降级 + 失败回滚） | M | 阶段 3（写指令需要 deviceId 定位） |
| 5 | 编辑器体验增强（撤销/多选/分辨率/拖放/注册机制） | L | 阶段 2（在持久化之上演进） |
| 6 | 安全、审计与版本管理（可选收尾） | M | 阶段 2、4 |

> 顺序依据：阶段 1 的 EF 迁移**一次性**把 Label 与绑定字段全部加齐，避免阶段 2/3 各自再迁移一次；0 与主线无依赖，可先行合入止血。

---

## 阶段 0：快速止血（独立小修，零风险）

### 0-1 组态画面状态类型兼容（新发现 P0）
- **问题**：`ScadaTopologyView.vue:53` `simulatedDataComputed` 判断 `d.status === 'online'`（字符串），而 P0-1 修复后 `mapRuntimeStatusToStatus` 产出数字 0–4 → 真机模式下组态画面组件值恒为 0。
- **改动**：判断改为 `(d.status === 'online' || d.status === 1)`，与 `dataOrchestration.ts:96` 的兼容写法对齐。
- **验收**：真机模式在线设备的变量值能驱动 HMI 组件动画。

### 0-2 NE 缩放手柄错绑
- **问题**：`CanvasPanel.vue:460` NE 手柄 `handleResizeStart($event, component, 'nw')`，且 `handleMouseMove` 无 `ne` 分支——NE 角缩放实际走 NW 逻辑。
- **改动**：绑定值改 `'ne'`；`handleMouseMove` 增加 `ne` 分支（与 `sw` 镜像：宽随 deltaX 反向、高随 deltaY 反向、x/y 同步移动）。

### 0-3 's'（南向）手柄定位异常
- **问题**：`CanvasPanel.vue:474` 手柄定位 `bottom-1/2 left-1/2`——`bottom-1/2` 使手柄落在组件垂直中部，不在底边。
- **改动**：改为 `-bottom-1.5 left-1/2`，与其它手柄定位约定一致。

### 0-4 组件 id 生成撞车风险
- **问题**：`ScadaTopologyView.vue` 两处 `${type}-${Date.now().toString().slice(-6)}`，同秒内快速「添加+复制」可能产生重复 id，导致 Vue key 冲突与误删。
- **改动**：抽 `genComponentId(type)` 工具：`crypto.randomUUID()` 或「type + 模块级自增序列」，全局唯一。

---

## 阶段 1：后端组态 API 补强（持久化地基）

**目标**：让三个孤儿接口从「能跑」变成「好用」，并把 schema 一次改到位。

### 1-1 DTO / 实体补字段
- `HmiComponent` 实体 + `HmiComponentDto` 增加 `Label`（string，前端组件标签，现只能塞 PropsJson 且前端 label 不在 props 里）。
- `HmiComponent` 增加 `BindDeviceId`（int?）+ `BindVariableKey`（string?）——为阶段 3 绑定模型预埋；旧 `BindField` 保留为兼容列（新库可恒空）。

### 1-2 一次性 EF 迁移
- 迁移名：`AddHmiComponentLabelAndBinding`（Label + BindDeviceId + BindVariableKey）。
- 因可删库重建，不做旧数据回填；`BindDeviceId` 加外键到 Devices（ON DELETE 行为：设备删除时绑定置 NULL 或级联拒绝——建议 SET NULL，画面组件保留但提示绑定失效）。

### 1-3 CRUD 语义修正（三组控制器同步）
- **Create 回填 Id**：`ScadaPageController / HmiComponentController / ScadaProjectController` 的 POST 改为 `CreatedAtAction` + 返回带 Id 的 DTO（前端现在拿不到新建 Id）。
- **Update 404 语义**：`AppService.UpdateAsync` 对不存在实体目前静默成功（返回 Ok 但什么都没做）——改为返回 bool 或抛 `BusinessException`，控制器转 404。
- **Delete 幂等**：不存在时返回 204 而非异常。

### 1-4 查询端点补齐
- `GET /api/ScadaPage?projectId={id}`：列表按项目过滤（现在 `GetListAsync()` 只能全量拉）。
- `GET /api/ScadaProject/{id}/full`：**推荐**——一次返回 工程→页面→组件 整树（替代现在「页面不带回组件、再单拉组件列表前端拼装」的两次往返）。

### 1-5 批量保存端点（核心新增）
- `PUT /api/ScadaPage/{id}/layout`：body 为该页**全量组件数组**（含布局与属性 JSON），事务内「删旧全量 + 批量插入」。
- 理由：画布每次拖动/改属性都是全量替换语义（`updateCurrentPageComponents`），逐条 CRUD 会产生海量单条 PUT；delete-all + insert 实现最简单、无 diff 复杂度、天然幂等。
- 服务端校验：组件 PageId 一致性、Name/Type 非空、X/Y/W/H 数值范围（DTO 加 DataAnnotations）。

### 1-6 存在性校验
- `HmiComponentAppService.CreateAsync` 校验 PageId 存在（防孤儿组件）；`ScadaPageAppService.DeleteAsync` 已有事务级联删组件，保留。

### 1-7 ScadaProject 同规格补齐
- `ScadaProjectAppService` 同样补 Create 回填 Id / Update 404；项目删除补级联删页面+组件（目前缺，需在事务内处理，或 DB 级联）。

**验收**：Swagger 全流程——建工程→建页→批量存布局→`full` 整树取回与提交一致；对不存在 Id PUT/DELETE 返回 404/204。

---

## 阶段 2：前端组态持久化打通（核心主线）

**目标**：组态画面从「内存 demo」变成「真持久化」，刷新不丢。

### 2-1 API 封装层
- 新建 `src/api/scadaApi.ts`：封装 Project CRUD、Page CRUD、`PUT layout`、`GET full`（走现有 `http.ts`，JWT 自动携带）。

### 2-2 id 双轨制（解决 string/int 冲突）
- `HMIComponent` 增加可选 `serverId?: number`；前端编辑态主键用 uid（阶段 0-4 的生成器），保存时服务端重生成 int Id 并回填 `serverId`；重新加载时以 serverId 建立映射。
- ScadaPage / ScadaProject 同理（前端 `project-xxx` 临时 id → 后端 int id）。

### 2-3 store 改造（scadaStore.ts 重写）
- 删除硬编码 3 工程与 `templates.ts` 引用；store 改为：`projects = ref([])` + `loading` + `loadScadaProjects()`。
- 加载时机：挂到 `App.vue` 登录成功 watch（与 `syncAreas / fetchDataModels / syncDevices` 并列预载）。
- 示例工程去向（二选一，**需确认**）：
  - **方案 A（推荐）**：后端 `DatabaseInitializer` seed 三个示例工程（templates.ts 的组件 JSON 移植为 C# 种子数据），新库开箱有演示内容；
  - **方案 B**：纯空态 + 前端「新建工程」引导，模板 JSON 保留在前端作「插入示例画面」按钮。
- `selectedProjectId / selectedPageId` 初始化改为「加载后取第一个」。

### 2-4 保存策略
- 显式「保存画面」按钮 + dirty 标记（任何 `updateCurrentPageComponents` 置脏）；保存成功清 dirty 并 toast。
- 路由离开拦截：`onBeforeRouteLeave` dirty 时弹确认（现有 confirm 即可，风格后续统一）。
- 可选增强：2s 防抖自动保存（先不做，保守范围）。

### 2-5 ScadaTopologyView 全部操作接 API
- 创建/重命名/复制/删除工程与页面 → 调 API 成功后更新 store（失败回滚 UI + toast），替换现在的纯内存 push/splice。
- 复制页面 = 取源页 `full` 数据 → POST 新页 → PUT layout。

### 2-6 联动收尾
- `DashboardView` 的组态画面数统计自动生效（store 已真实化）；`addLog` 组态日志保留，后续阶段 6 接后端审计。

**验收**：新建工程 → 拖入并布置组件 → 保存 → F5 刷新 → 画面完整还原；断网保存失败时 dirty 保留并有明确提示。

---

## 阶段 3：变量绑定模型升级（P0-3 落地）

**目标**：绑定从「裸 key 假全局唯一」升级为 `deviceId + variableKey` 复合定位。

### 3-1 类型与绑定 UI
- `types.ts`：`HMIComponent` 增加 `bindDeviceId?: number | null`、`bindVariableKey?: string`（`bindField` 保留兼容）。
- `InspectorPanel` 绑定下拉改两级：设备（在线优先）→ 该设备变量（从 `devices` + `variableMeta` 取，含单位/只读标记），显示 `设备名 · 变量名`；`plcTagsList` 从「dataModels 压平去重」废弃。

### 3-2 实时值数据流
- `simulatedDataComputed`：由「所有在线设备变量压平一个 map（同名覆盖）」改为 `Record<'${deviceId}:${key}', value>`；CanvasPanel 取值 `simulatedData['${c.bindDeviceId}:${c.bindVariableKey}']`。
- 旧画面兼容（过渡期）：仅有 `bindField` 时按旧逻辑取第一个命中，并在 Inspector 显示「旧绑定，建议重新选择」。

### 3-3 读写工具函数
- `dataOrchestration.ts` 的 `getDeviceVariableValue / setDeviceVariableValue` 增加 `(deviceId, variableKey)` 参数重载，旧签名内部代理到「全设备扫描」（过渡期）。
- 保存时把 `bindDeviceId / bindVariableKey` 一并写入 layout（阶段 1 schema 已就位）。

**验收**：两台设备存在同名 key 时，画面两个组件分别显示各自设备值，互不覆盖；绑定下拉能区分设备。

---

## 阶段 4：控制写入链路打通

**目标**：真机模式下 HMI 按钮写指令真正到达设备，失败可见可恢复。

### 4-1 后端 REST 写端点（权威通道）
- `POST /api/TelemetryData/{deviceId}/variables/{variableKey}/write`，body `{ value }`。
- 校验链：设备存在且在线 → 变量存在 → `effectiveIsReadOnly = false`（设备级覆盖优先）→ 经 RuntimeManager 写驱动 → 成功后由现有 `SignalRNotificationService` 广播 `ReceiveVariableUpdate`（写后回读广播）。

### 4-2 ScadaHub 上行方法（二选一，**需确认**）
- **方案 A（推荐）**：**不实现** Hub 上行，前端写指令统一走 4-1 REST——天然吃全局 JWT FallbackPolicy，鉴权零成本，实现最少；`设计规范.md` 中 `WritePlcVariable` 一节同步修订。
- **方案 B**：Hub 实现 `WritePlcVariable(deviceId, variableKey, value)`——需解决 ScadaHub 目前 `[AllowAnonymous]` 的鉴权问题（access token 经 query string / `withAccessTokenFactory` 传递 + 方法内校验），复杂度高，仅在写频极高时才值得。

### 4-3 前端写链路修正
- `dataOrchestration.writeVariableToBackend`：现在只打日志「正在尝试 REST 写入」但**没有任何降级实现**——按 4-2 所选方案补真实调用；参数升级为 `(deviceId, variableKey, value)`。
- **写失败回滚**：写前快照旧值，REST 失败恢复快照 + toast 报错（当前失败只 addLog，UI 显示与设备实际值不一致）；成功后以 SignalR 推送值为准（乐观更新保留，推送到达即覆盖为真值）。

### 4-4 只读与权限前置
- 运行模式按钮/开关：按 `variableMeta[key].effectiveIsReadOnly` 禁用写行为（`CanvasPanel.handleDragStart` 前置判断），只读变量点击仅提示不可写。
- 角色限制（放到阶段 6 完整做，此处先留 TODO 挂点）。

**验收**：真机模式点击阀门按钮 → 设备值变化 → SignalR 回推所有客户端画面同步；写只读变量被拦截；后端停止时写失败、UI 回滚且提示。

---

## 阶段 5：编辑器体验增强（P2 清偿）

### 5-1 撤销 / 重做
- 命令栈：add / delete / update（move、resize、属性变更统一归并为 update，可用组件快照 diff），Ctrl+Z / Ctrl+Shift+Z，上限 50 步；清空画布入栈（可撤销）。
- 落点：`ScadaTopologyView` 包一层 `historyService`（纯前端，不影响后端）。

### 5-2 多选与对齐
- Shift/Ctrl 点选 + 空白处拖拽框选；批量移动 / 删除 / 复制；对齐工具条（左/右/上/下/水平居中/垂直居中/等距分布）——替换现在「置顶对齐=移动到 y:10」的假对齐（`CanvasPanel.alignComponents`）。

### 5-3 画布自定义分辨率
- `ScadaPage` 实体加 `Width / Height`（阶段 1 迁移一并加，默认 1100×700）；新建页面时选预设（1920×1080 / 1366×768 / 1100×700 / 自定义）；CanvasPanel 画布容器尺寸改由页面属性驱动（现在硬编码 `h-[700px] w-[1100px]` 与文案「画布尺寸: 1100 × 700」）。

### 5-4 组件库拖拽投放
- WidgetLibrary 卡片支持 HTML5 drag（或 pointer 拖放）→ 画布落点即组件坐标（按 zoom 反算 + 网格吸附），替代现在「点击固定落 (40,60)」。

### 5-5 组件注册机制（消除四处散改）
- 新建 `widgetRegistry.ts`：`type → { 默认尺寸, 默认 props, 属性面板 schema, 图标 }` 单一注册点；`WidgetLibrary` 列表、`InspectorPanel` 属性表单改 **schema 驱动**；新增组件类型只改注册表 + HMIWidget 渲染分支两处。
- `InspectorPanel`（403 行 if/else 模板）按 schema 拆分为通用字段组件 + 分组折叠。

### 5-6 危险操作体验
- 清空画布/删除组件由原生 `confirm/alert` 换统一模态确认（项目已有 Toast 体系，可扩展轻量 Modal）。

---

## 阶段 6：安全、审计与版本管理（可选，按需排期）

### 6-1 组态编辑审计
- layout 保存、工程/页面增删改名 → 写后端 `ConfigLog`（操作人 JWT claims、对象、变更摘要）；前端内存 `addLog` 保留为即时反馈。

### 6-2 写控制角色权限
- 后端：写端点（4-1）加 `[Authorize(Roles = "Operator,Admin")]`（角色体系沿用现有 SystemUser）。
- 前端：非授权角色运行模式隐藏写控件；写操作留后端兜底校验。

### 6-3 组态版本管理（远期）
- `ScadaPageRevisions` 表：每次保存存快照（JSON），支持查看历史与回滚；进一步可做「草稿/发布」双态（IsPublished），运行模式只加载已发布版本——工业现场防止「边改边看」的半成品画面。

---

## 总验收清单（跨阶段回归）

1. F5 / 重启后端，组态工程、页面、组件、绑定完整还原（阶段 2）。
2. 两台设备同名变量互不干扰，绑定下拉可区分设备（阶段 3）。
3. 真机模式按钮写值 → 设备变化 → 所有客户端画面同步刷新；写失败 UI 回滚（阶段 4）。
4. 撤销重做、多选对齐、自定义分辨率、拖放添加全部可用（阶段 5）。
5. Swagger 上三个组态控制器语义正确（201 回 Id / 404 / 204）（阶段 1）。

## 风险与边界

- **范围控制**：阶段 5、6 属增强项，可在阶段 4 完成后按实际优先级裁剪或推后。
- **两个待确认决策点**：① 阶段 2-3 示例工程去留（A 后端 seed / B 前端空态+模板按钮）；② 阶段 4-2 写通道形态（A 仅 REST，推荐 / B Hub 上行）。
- **删库重建约定**：所有迁移不回填旧数据；若届时库中有不想丢的画面，需先补回填脚本再执行本方案。
