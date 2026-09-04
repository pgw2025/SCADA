/**
 * 薄转发层：保持全部既有导出签名不变，9 个依赖文件零改动（审查 A8）。
 * 实现已迁移：
 *  - 渲染器 / lucide 图标 → builtinRenderers.ts
 *  - 本地兜底种子 / 静态辅助（控制类集合 / 菜单图标 / 调色板）→ builtinSeeds.ts
 *  - 运行时模板源与两级匹配 → widgetTemplates.ts
 *
 * 依赖方（rg "widgetRegistry" Client/src 复核）：CanvasPanel / InspectorPanel /
 * TrendChartInspector / NavMenuInspector / ScadaTopologyView / LayersPanel /
 * useWidgetBase / WidgetLibrary / NavMenuWidget —— 均无需改动。
 */
export type { WidgetCategory, WidgetDef } from './widgetTemplates';
export { widgetList, getWidgetDef } from './widgetTemplates';
export {
  MENU_ICON_OPTIONS, getMenuIcon,
  CONTROL_WIDGET_TYPES, isControlWidget,
  TREND_SERIES_PALETTE,
} from './builtinSeeds';
