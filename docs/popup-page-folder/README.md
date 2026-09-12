# 弹窗列表文件夹化改造（popup-page-folder）

给组态设计页面（`ScadaTopologyView.vue`）左侧面板的「🪟 弹窗」分组增加文件夹分类管理，对齐已落地的桌面端/移动端画面文件夹能力（见 `docs/scada-page-folder/`）。**本目录仅存放设计与实施文档，不含源码改动。**

## 方案性质（评审结论）

本方案是在初版方案（复用 `ScadaPageFolder` + Platform 放行 Popup）基础上，经代码级复核后修订而成。复核发现并修正了初版的 2 处错误：

1. **`fromFolderDto` 漏改（P0）**：`scadaApi.ts` 中该函数把非 `Mobile` 的 platform 一律映射为 `Desktop`，若不改，刷新后 Popup 文件夹会错误显示到桌面端树、弹窗树里消失。初版误判为"API 层无需改动"。
2. **唯一索引论据不成立**：`ScadaPageFolders` 表并无 `(ProjectId, Platform, ParentFolderId, Name)` 唯一索引，仅有两个普通索引；同名防重完全依赖应用层 `EnsureNameUniqueAsync`（四元组校验，对 Popup 语义正确）。"无需迁移"的结论仍成立，但论据修正为：`Platform` 为 varchar(16) 无 CHECK 约束、应用层校验已按 Platform 隔离。

并补齐了初版遗漏的改动点：`persistFolderReorder` / `reindexAndPersistSegment` 的类型扩展、弹窗组头部新建根文件夹按钮、根级拖放容器、`RenormalizeOrderAsync` 预存缺陷修复建议等。

## 最终选型

| 决策点 | 结论 |
|---|---|
| 数据模型 | **复用现有 `ScadaPageFolder` 表**，`Platform` 字段放行 `'Popup'`；**无数据库迁移** |
| 后端改动 | **3 处拦截放行**（`ValidatePlatform` / `NormalizePlatform` / `ResolveFolderIdAsync`），无新实体/表/AppService/Controller/DTO |
| 前端结构 | 弹窗分组**平铺列表替换为 `ScadaPageTree` 递归树**（与桌面/移动端同一组件） |
| 弹窗语义 | 文件夹为**纯设计态组织手段**；弹窗无「设为首页」概念，树中隐藏 Home 按钮 |
| 存量数据 | 现有弹窗 `FolderId` 全 NULL，**自动落弹窗根级，无需回填** |

## 文档索引

| 文档 | 内容 |
|---|---|
| [01-落地方案.md](./01-落地方案.md) | 现状拦截点清单、总体方案、P0/P1/P2 分级改动清单（精确到文件与位置）、明确不做的事 |
| [02-执行计划与验收.md](./02-执行计划与验收.md) | 分阶段执行步骤、每步验证点、验收用例清单、回归风险与已知行为 |

## 核心要点

- 树 = **Platform（桌面端/移动端/弹窗）→ 文件夹（可嵌套）→ 画面**，三端共用同一套 `ScadaPageFolder` 数据与 `ScadaPageTree` 组件。
- 唯一的"新逻辑"是放行：后端 3 处拦截解除 + 前端 platform 类型联合扩展 + `fromFolderDto` 三分支映射；其余（嵌套/拖拽/排序/删除/防重）全部继承现有实现。
- 运行态零改动：`ScadaPlayerView` 仍只认 Desktop/Mobile，弹窗经 `PopupPageHost` 按 id 调起，文件夹不参与运行逻辑。
- 导出/导入不含文件夹信息（导入落目标工程弹窗根级）、复制弹窗不保留 `folderId`（副本落根级）——均与桌面/移动端既有行为保持一致。
