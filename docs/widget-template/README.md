# 组态组件库动态化（方案 A+B）：模板入库 + SVG 模板渲染引擎

> 文档版本：v2.0（含审查修正）
> 基线分支：feature/runtime-refactor
> 状态：设计评审完成，待实施

## 1. 目标

把组态设计页面（ScadaTopologyView）的组件库从「编译期硬编码」改造为「运行时数据驱动」，实现：

1. **组件库不写死**：组件列表 / 分类 / 默认尺寸 / 默认属性 / 图标 / 排序全部来自数据库模板表；
2. **组件可管理**：模板支持新增、编辑、删除（系统内置模板保护）、排序、显示/隐藏；
3. **组件可迁移**：模板支持导出为 JSON 文件、从 JSON 文件导入（支持覆盖 / 重命名两种冲突策略）；
4. **零代码新建图形组件**：通过 SVG 模板 + 占位符绑定（方案 B），无需修改前端代码即可创建新的展示类图元。

## 2. 核心决策表（D1～D11）

| #   | 决策                       | 说明                                                                                                                                                     |
| --- | ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| D1  | 画布落库仍写 RenderType        | `HmiComponent.Type` 保持存渲染类型（`boiler`/`title-header`…），模板变体（desktop/mobile）靠 `defaultProps` 区分。**InspectorPanel（11 处 type 判断）与 CanvasPanel（约 10 处）零改动** |
| D2  | TemplateKey 两级匹配         | 模板唯一键 = 注册键（如 `title-header-tech-desktop`），冗余 `RenderType` 列。`getWidgetDef()` 先按 key 精确匹配、再按 renderType 泛化匹配 → 存量数据与新模板都命中                             |
| D3  | 双渲染轨                     | `RenderKind='builtin'` 走现有 23 个 SFC；`RenderKind='svg'` 走通用 `SvgTemplateWidget`                                                                         |
| D4  | 快照语义                     | 模板默认值只影响"新放置"的组件；已放置组件的 `PropsJson` 快照不变更                                                                                                              |
| D5  | 种子化 + 系统保护               | 首次启动把现有 **24 个注册键**（22 个唯一渲染类型 + 4 个复合键变体）写入模板表，`IsSystem=true` 仅可编辑/隐藏不可删除                                                                            |
| D6  | 图标字符串化                   | 模板存 `IconKind`（lucide/div/svg/emoji）+ `IconKey` + `IconColor`（Tailwind 类）字符串；前端维护 lucide 名→组件映射                                                        |
| D7  | SVG 双清洗                  | 后端入库前清洗（存库即安全），前端渲染前再清洗（纵深防御）；白名单 URL 仅 `#` / `data:image/` / `/api/`                                                                                  |
| D8  | 权限                       | 模板读取 = 登录用户（运行态渲染依赖）；模板增删改/导入 = `RequireAdmin` 策略；管理页路由 `meta: { roles: ADMIN_ROLES }`                                                                 |
| D9  | ComponentType 放宽为 string | `HMIComponent.type`、`handleAddWidget` 参数、`WidgetDef.type` 从 `ComponentType` 联合类型放宽为 `string`（联合类型保留为内置类型提示）。所有 `=== 'xxx'` 字符串比较不受影响，仅类型标注放宽           |
| D10 | SVG 模板键唯一                | 自定义 SVG 模板 `TemplateKey = RenderType`（同一值），保证 `getWidgetDef` 精确命中与 `defDefaults` 兜底正确                                                                  |
| D11 | 批量导入导出                   | 单模板导出（下载 `.widget.json`）+ 批量导出（`templates` 数组）；导入支持单/数组两种载荷                                                                                            |

## 3. 文档导航

| 文档                                 | 内容                                                                                       |
| ---------------------------------- | ---------------------------------------------------------------------------------------- |
| [01-方案审查报告.md](01-方案审查报告.md)       | 对 v1.0 方案的审查结论：必须修正的问题（13 项）、设计优化（10 项）、修正后的方案差异摘要                                       |
| [02-执行计划.md](02-执行计划.md)           | **核心文档**：7 个阶段 / 28 个步骤 / 每步具体任务、产出物、验收标准、回退策略                                           |
| [03-后端详细设计.md](03-后端详细设计.md)       | 实体 / DTO / 仓储 / AppService / Controller / SvgSanitizer / 种子数据的代码级设计                      |
| [04-前端详细设计.md](04-前端详细设计.md)       | builtinRenderers / builtinSeeds / 模板 store / widgetRegistry 转发层 / HMIWidget 双轨渲染 / 模板管理页 |
| [05-数据协议与种子清单.md](05-数据协议与种子清单.md) | 导入导出 JSON Schema、SVG 占位符规范、24 条内置种子数据总表、模板生命周期规则                                         |
| [06-验收与风险.md](06-验收与风险.md)         | 分阶段验收清单、风险登记表、回滚预案                                                                       |

## 4. 里程碑概览

| 阶段 | 名称            | 覆盖需求                  | 依赖    | 状态    |
| -- | ------------- | --------------------- | ----- | ----- |
| P0 | 准备与基线         | 分支、审计、类型放宽            | 无     | ✅ 已实施 |
| P1 | 后端模板管理        | 模板 CRUD / 种子          | P0    | ✅ 已实施 |
| P2 | 前端模板源切换       | 组件库动态化（不写死）           | P1    | ✅ 已实施 |
| P3 | 管理页 + 导入导出    | 添加 / 删除 / 编辑 / 导入导出   | P2    | ✅ 已实施 |
| P4 | SVG 渲染引擎      | 零代码新建组件               | P3    | ✅ 已实施 |
| P5 | 属性面板 Schema 化 | InspectorPanel 收敛（可选） | P3    | ✅ 已实施 |
| P6 | 回归验收          | 全量回归                  | P1～P5 | ✅ 已完成 |

> P1～P3 完成 即满足「不写死 + 增删改 + 导入导出」全部原始诉求；P4/P5 为增强能力，可按需排期（均已落地）。
> 实施偏差与实况回填见各详细文档标注；P6 回归结论见 [06-验收与风险.md](06-验收与风险.md)。

## 5. 关键约束（继承项目硬约束）

- 前端 API 请求使用相对路径（经 Vite 代理转发），模板 API 遵循 `${API()}/HmiWidgetTemplate` 现有模式；

- MySQL 索引列使用 VARCHAR 显式长度（`TemplateKey(64)` 唯一索引）；

- 后端统一异常：`BusinessException` → `ApiResponse`（`{ success, message, errors }`），成功返回裸 DTO（`Ok(dto)`），前端 `r.data` 直取；

- 控制器入参守卫沿用 `ApiControllerBase.EnsureBody<T>()`；

- 所有时间戳 UTC 落库。

