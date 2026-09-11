# 运行态弹窗（openPopup）方案

本目录承载「组态运行界面弹窗」功能（方案 A）的完整设计文档与后续执行记录。

## 背景

需求：在组态运行界面，点击图元（如电机图标）弹出对话框显示变频操作面板，且只有操作员/管理员可操作。

经调研，变频操作面板组件（`vfd-motor-panel` / `VfdMotorPanelWidget.vue`）已存在并内置角色权限判断，
本功能实质是补齐「运行界面弹出一个面板窗口」的通用能力，而非新建面板。

## 文档索引

| 文档 | 说明 |
| --- | --- |
| [01-方案设计.md](./01-方案设计.md) | 方案 A 完整设计（v1.1 修订版）：调用链、参数 schema、PopupHost 设计、数据流、权限、边界、约束核对、验收清单 |
| [02-评审修订记录.md](./02-评审修订记录.md) | 代码级评审：P1–P3 事实修正、E1–E6 边界补充、可选优化与 v1.0→v1.1 修订对照 |
| [03-实施计划.md](./03-实施计划.md) | **实施主计划**：5 阶段 16 步，每步含任务/文件/具体内容/验证/注意，附关键设计决策、风险回滚与工作量估算 |

## 相关代码位置

- 事件分发：`Client/src/services/hmiEventService.ts`
- 事件动作类型/参数：`Client/src/types.ts`（`HmiEventActionKind` / `HmiEventAction`）
- 设计态事件编辑器：`Client/src/components/EventPanel.vue`
- 运行态宿主：`Client/src/components/ScadaPlayerView.vue`
- 画布点击分发：`Client/src/components/CanvasPanel.vue`
- 复用面板组件：`Client/src/components/widgets/VfdMotorPanelWidget.vue`
- 组件渲染分发：`Client/src/components/HMIWidget.vue`

## 状态

- [x] 方案可行性分析与设计（v1.0）
- [x] 代码级评审与修订（v1.1，见 02-评审修订记录）
- [x] 实施计划编制（5 阶段 16 步，见 03-实施计划）
- [ ] 待实施（按 03-实施计划 阶段 0 → 阶段 4 顺序执行）
- [ ] 待回归验收（对照 01-方案设计 §11 验收清单 19 条）