# 实时波段趋势图（trend-chart）多变量绑定改造设计

> 目标：让 `trend-chart` 支持 **多变量绑定**，且 **每个变量可自定义线条颜色与粗细**。
> 状态：设计草案（待评审）。本文只给方案，**未改动任何代码**。

---

## 0. 现状（As-Is，关键 file:line 证据）

| 环节 | 位置 | 现状 |
|---|---|---|
| 注册定义 | `widgetRegistry.ts:282-293` | `trend-chart` 仅 `name/icon/description`，无系列概念 |
| 绑定模型 | `ScadaTopologyView.vue:261-289`（`componentValues` 计算属性） | 每组件**仅一个值**：`result[c.id] = composite[`${bindDeviceId}:${bindVariableKey}`]` |
| 数据源 | `ScadaTopologyView.vue:291-296`、`ScadaPlayerView.vue:156-165` | `watch(componentValues)` → 单序列 `pushTrendPoint(c.id, vals[c.id] ?? 0)` |
| 缓冲结构 | `utils/trendHistory.ts:7` | `trendHistory: Record<componentId, number[]>`（**每组件一条线**） |
| 传参 | `CanvasPanel.vue:962` | `:history="trendHistory[component.id]"` → `HMIWidget.props.history?: number[]` |
| 渲染 | `HMIWidget.vue:726-758`（`chartPath`）、`1158-1184` | **仅一条** `<path>`，颜色写死 `isHighAlert?'#ef4444':'#10b981'`，线宽写死 `2.5`；表头只读单个 `numValue` |
| 设备订阅 | `ScadaTopologyView.vue:309-319`、`ScadaPlayerView.vue:91-99`（`boundDeviceIds`） | 仅订阅 `c.bindDeviceId`（单设备），多设备序列收不到推送 |
| 属性面板 | `InspectorPanel.vue`（趋势图分支 `1165/1192`） | 仅有单变量 `bindDeviceId`/`bindVariableKey` 绑定 UI，无系列增删 |

**结论**：当前是一条"单变量 → 单缓冲 → 单 path"的硬链路。要做多变量，需从**绑定模型 → 数据源 → 缓冲结构 → 渲染 → 订阅 → 属性面板**六处同步改造；否则只改渲染会拿不到多序列数据，只改绑定会收不到推送。

---

## 1. 目标数据模型

新增 `HmiTrendSeries`（复用 `HmiDashboardItem` 的 `deviceId`/`variableKey`/`label`/`unit`/`precision` 范式，并加图形属性）：

```ts
// types.ts 新增
export interface HmiTrendSeries {
  id: string;                 // 稳定主键（缓冲 key，重排/增删不变）
  deviceId?: number | null;  // 绑定设备；空则继承组件 bindDeviceId / 全局首设备
  variableKey: string;       // 变量键名
  label?: string;            // 图例名称（空则取变量模板名/键名）
  unit?: string;             // 单位（空则继承）
  color: string;             // 线条颜色（必填，默认调色板轮转）
  lineWidth: number;         // 线条粗细（px，默认 2）
  minValue?: number | null;  // 该序列量程下限（空→参与全局自适应）
  maxValue?: number | null;  // 该序列量程上限
  precision?: number | null; // 小数位
  thresholdMin?: number | null;
  thresholdMax?: number | null;
}
// HMIComponent['props'] 新增：
//   trendSeries?: HmiTrendSeries[];   // 与 dashboardItems 并列（types.ts:235 附近）
```

---

## 2. 改造范围与 Before/After（文件级）

### 2.1 `types.ts`
- **Before**：无 `HmiTrendSeries`。
- **After**：新增接口 + `HMIComponent['props'].trendSeries?: HmiTrendSeries[]`。

### 2.2 `utils/trendHistory.ts`（⚠️ 共享结构，风险最高）
- **Before**：`Record<componentId, number[]>`；`pushTrendPoint(cid, v)`、`clearTrendHistory(cid)`。
- **After**：`Record<componentId, Record<seriesId, number[]>>`；
  `pushTrendPoint(componentId, seriesId, value)`、`clearTrendHistory(componentId)`（清空该组件下所有序列）、可选 `getSeriesMap(cid)`。
- `CanvasPanel.vue:962` 仍写 `:history="trendHistory[component.id]"`，类型由 `number[]` 变为 `Record<string, number[]>`，**调用点几乎不动**。

### 2.3 `ScadaTopologyView.vue` / `ScadaPlayerView.vue`（两处对称）
- **数据源 watch（Before）**：`pushTrendPoint(c.id, vals[c.id] ?? 0)`。
- **数据源 watch（After）**：遍历 `trend-chart` 的 `trendSeries`（空则回退单绑定构造 1 条），对每条序列从 `devices` store 解析当前值（同 `dashboardResolvedItems` 的 `dev.variables[key]` 解析法）后 `pushTrendPoint(c.id, s.id, v ?? 0)`。仍挂在 `watch(componentValues)` 上（其随任意变量变化重算，天然触发推送），回调内读取 `devices.value` 解析各序列——只读不追踪，安全。
  - 复用既有"值未变化不推点"优化（避免同值刷屏，`trendHistory.ts:14-15`）。
- **`boundDeviceIds`（Before）**：仅 `c.bindDeviceId`。
- **`boundDeviceIds`（After）**：新增 `trend-chart` 分支，把每个 `trendSeries[].deviceId`（及回退 `bindDeviceId`/首设备）纳入订阅集合——**与 multi-var-dashboard 修复完全同构**（已落地 commit `3c9729b`）。

### 2.4 `HMIWidget.vue`
- **Before**：`props.history?: number[]`；`chartPath` 产出单条 path；模板单 `<path :d="chartPath" :stroke=... stroke-width="2.5">`；表头单个 `numValue`。
- **After**：
  - `props.history?: Record<string, number[]>`。
  - 新增 `chartSeries` computed → `Array<{ id, d, color, lineWidth, label, current, unit, alert }>`：对每条序列用其 `minValue/maxValue`（或全局自适应）做 Y 归一化，产出对应的 `d` 与图形属性；`current` 取该序列末尾值。
  - 模板：`<path>` 改为 `v-for` 多序列渲染，`:stroke="s.color"`、`:stroke-width="s.lineWidth"`。
  - 表头：单值读数 → **图例列表**（色块 + label + 当前值 + 单位，每条一行）。
  - `hasTrendData` / `trendReady`：改为"存在 ≥1 序列且其缓冲 ≥2 点"。

### 2.5 `InspectorPanel.vue`
- 新增"**趋势系列**"编辑区，结构**镜像 multi-var-dashboard 子项编辑器**（`InspectorPanel.vue:278-323` 的 `commitDashboardItems` / `importAllVariablesFromDevice` / `getItemVariableOptions`）：
  - 系列列表 `v-for`：每条含 设备下拉、变量下拉、label、颜色选择、线宽（数字/滑块）、可选量程/阈值；支持 增/删/上下移。
  - "从设备导入全部变量"按钮（复用 `importAllVariablesFromDevice` 思路，写入 `trendSeries`，并自动分配轮转颜色 + 默认线宽）。
  - 原单变量 `bindDeviceId`/`bindVariableKey` 入口：保留为**兼容回退**（见决策 D4）。

### 2.6 `widgetRegistry.ts`
- `trend-chart` 的 `defaultProps` 追加 `trendSeries: []`，并定义一套默认线条调色板（如 `['#10b981','#3b82f6','#f59e0b','#ef4444','#a855f7']`）供"导入全部"时轮转分配。

---

## 3. 需要你拍板的决策点（D1–D4）

### D1. 多序列数据如何采集（影响耦合度）
- **D1-A（推荐）**：数据源 watch 回调内**直接读 `devices` store** 解析每条序列当前值（与 `dashboardResolvedItems` 同范式），`trendHistory[componentId][seriesId]` 存缓冲。不改动共享的 `componentValues`。
- D1-B：扩展 `componentValues` 产出 `componentId::seriesIndex` 复合键。改动面更大、影响所有控件。

### D2. `trendHistory` 存储形态
- **D2-A（推荐）**：`Record<componentId, Record<seriesId, number[]>>`，每组件一个 map，`CanvasPanel` 传 `trendHistory[component.id]` 几乎零改动。
- D2-B：扁平 `componentId::seriesId` 键，需按前缀过滤，较 hacky。

### D3. Y 轴量程
- **D3-A（推荐）**：**默认全局共享自适应**（多条线同一坐标轴便于对比），序列显式配了 `minValue/maxValue` 时改用该序列量程。
- D3-B：每条线各自独立归一化（失去同比意义，不推荐）。

### D4. 旧单变量绑定的兼容
- **D4-A（推荐）**：加载时**一次性把旧 `bindDeviceId`/`bindVariableKey` 迁移进 `trendSeries[0]`**（写入 props），属性面板统一收敛到系列编辑器；旧图照常工作。
- D4-B：保留旧字段作为独立"快速单绑"入口，`trendSeries` 为空时才用旧字段。双轨更乱。

> 其余（图例显示 D5、序列级阈值报警 D6）默认按推荐实现，无需额外决策。

---

## 4. 风险与验证

- **风险**：`trendHistory` 为 3 处共享（2 个运行视图 + CanvasPanel），改形态须三处原子同步，否则运行态崩溃。
- **回归**：既有单变量趋势图（含已落库设计）必须仍可显示——靠 D4 回退/迁移保证。
- **验证步骤**：
  1. 新建 `trend-chart` → 从设备"导入全部变量" → 多条不同颜色/粗细曲线实时绘制；
  2. 编辑某序列颜色/线宽 → 图例与线条即时变化；
  3. 绑定跨 2+ 台设备变量 → 全部实时更新（验证 `boundDeviceIds` 订阅覆盖）；
  4. 旧单变量趋势图打开仍正常（验证 D4 兼容）；
  5. 切换/关闭页面 → 缓冲清理无残留（验证 `clearTrendHistory`）。

---

## 5. 待确认后再落地
请对 **D1 / D2 / D3 / D4** 给出选择（默认推荐项可直接回 "按推荐"），确认后我再分步改代码并提交（不 push，等你确认）。
