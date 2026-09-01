# InspectorPanel.vue 拆分重构设计文档

> 状态：方案已确认，待设计文档审批后动手写代码
> 日期：2026-09-01
> 目标文件：`Client/src/components/InspectorPanel.vue`（当前 2681 行）

---

## 1. 背景与目标

`InspectorPanel.vue` 是 SCADA/HMI 编辑器右侧"属性检查器"，按当前选中对象类型渲染不同表单。

| 指标 | 数值 |
|---|---|
| 总行数 | 2681 |
| `<script setup>` | ~547 行（已按功能注释切成 ~13 块，结构清晰） |
| `<template>` | ~2133 行（占 80%，主要问题） |
| `<style>` | 无 |

模板本质是一个超长 `v-if` 分发链：按 `selectedComponent.type` 给每种控件渲染一块专属表单，外加 `backgroundPage` 分支（页面属性）。

**目标**：拆为「调度壳 + 按控件类型的子组件 + 公共组件」，行为零变化（behavior-preserving）。

**红线（拆分必须守住）**：所有修改最终只走两个 emit ——
- `emit('updateComponent', id, { props: {...} })`（脚本 `InspectorPanel.vue:24`、`68-84`）
- `emit('updatePage', updates)`（脚本 `:25`、`:432`）

防抖落库在上游（画布/store 监听这两个事件），本组件只是**桥**，子组件无需复制落库逻辑。

---

## 2. 已确认决策（D1–D4；D5 延期）

| 决策 | 结论 |
|---|---|
| **D1** emit 桥接 | 子组件 `emit('update:component' / 'update:page')`，由父组件**转发**为自身 `updateComponent` / `updatePage`。子组件不直接调 store。 |
| **D2** 子组件入参 | 子组件接收类型化的 `component: HMIComponent` + **显式 store 切片**（如 `devices`、`systemScripts`）作为 props。 |
| **D3** 拆分范围 | **本阶段先只拆子组件**，不抽 composables（脚本区逻辑按需内联到对应子组件）。 |
| **D4** 公共组件粒度 | 外观/状态/量程/阈值等并入 `CommonWidgetSection.vue`（靠 `component.type` 的类型数组条件控制显隐）。 |
| **D5** 列表通用 composable | 随 D3 一并**延期**：`menuItems` / `dashboardItems` / `trendSeries` 三处增删移逻辑**内联**在各自子组件，暂不做 `useListEditor`。 |

---

## 3. 当前结构（真实 file:line 引用）

### 3.1 模板主分支

| 区块 | 行号 | 归属 |
|---|---|---|
| 空态（未选中） | `550–570` | 壳内联 |
| `backgroundPage` 分支（页面属性） | `571–866` | `PageBackgroundInspector.vue` |
| `v-else` 主区（公共 + 各类型） | `867–2681` | 见下 |

### 3.2 公共区（并入 `CommonWidgetSection`）

| 子块 | 行号 |
|---|---|
| Core Layout 区 | `886` 起 |
| Layer 分配 & 元件状态 | `944` 起 |
| PLC Register 绑定选择器 | `988` 起 |
| Widget 通用外观（showLabel / 边框 / 背景 / 内部标签 / 报警联动） | `1029` 起 |
| 状态文案解耦（阀/数显/开关等） | `1203` 起 |
| Medium Fluid Filler 样式 | `1203` 区 |
| 量程 / 高·低限报警阈值 | `1203–1309` |

### 3.3 各控件专属区

| 控件 | 模板行号 | 脚本逻辑行号 |
|---|---|---|
| `trend-chart`（多序列） | `1310–1556` | `328–426`（`trendSeries` + 迁移 watch） |
| `var-display`（数据显示） | `1557–1614` | （共用通用区） |
| `button`（导航/设值） | `1615–1662` | `171–204`（复合绑定） |
| `nav-menu`（菜单项） | `1663–1784` | `205–242` |
| `image`（图元换图） | `1785–1818` | `457–471` |
| `title-header`（三套风格） | `1819–1932` | （内联） |
| `rounded-btn`（圆角按钮） | `1933–2191` | `86–170`、`427` 外、`125–138`、`2037` 区 |
| `sys-time`（时钟格式） | `2192–2208` | （内联） |
| `multi-var-dashboard`（看板） | `2209–2638` | `243–327` |
| `text/button/rounded-btn` 字体块 | `2639–2681` | （内联，并入对应子组件或公共区） |

### 3.4 脚本核心辅助（随对应子组件迁移）

| 函数 | 行号 | 去向 |
|---|---|---|
| `updateProp` / `applyProps` / `numInput` / `typeDefaults` / `componentProps` | `29–84` | 子组件内联（同源逻辑） |
| `roundedBtnPresets` / `applyRoundedBtnPreset` | `86–119` | `RoundedButtonInspector` |
| `scriptListRequested` watch | `125–138` | `RoundedButtonInspector` |
| `opBinding*` / `numInput` | `139–170` | `RoundedButtonInspector` |
| `bindingVariableOptions` / `onBind*` | `171–204` | 需绑定的子组件（button/rounded-btn） |
| `menuItems*` | `205–242` | `NavMenuInspector` |
| `dashboardItems*` / `getItemVariableOptions` | `243–327` | `MultiVarDashboardInspector` |
| `trendSeries*` / 迁移 watch | `328–426` | `TrendChartInspector` |
| `pageBackground` / `updateBackground` / 类型切换 | `427–456` | `PageBackgroundInspector` |
| 图片/背景图库选图 | `457–471` | `ImageInspector` / `PageBackgroundInspector` |
| `THEME_CANVAS_PRESETS` / `applyThemePreset` | `472–546` | `PageBackgroundInspector` |

---

## 4. 目标文件结构

```
Client/src/components/
├── InspectorPanel.vue              # 壳：props/emit 契约 + 空态 + 分发
└── inspector/
    ├── CommonWidgetSection.vue     # 公共布局/图层/绑定/外观/状态/量程/阈值
    ├── PageBackgroundInspector.vue # 页面属性（背景 + 自适应 + 主题预设 + 图库）
    ├── VarDisplayInspector.vue
    ├── ButtonInspector.vue
    ├── NavMenuInspector.vue
    ├── ImageInspector.vue
    ├── TitleHeaderInspector.vue
    ├── RoundedButtonInspector.vue  # 最大块
    ├── SysTimeInspector.vue
    ├── TrendChartInspector.vue
    └── MultiVarDashboardInspector.vue
```
> `text` 字体块（`2639–2681`）：实现时决定并入 `CommonWidgetSection` 的 type 门控，或新建极简 `TextInspector.vue`。倾向前者，避免新增文件。

---

## 5. 接口契约

### 5.1 父组件（壳）—— props/emit 不变

```ts
const props = defineProps<{
  selectedComponent: HMIComponent | null;
  currentPageId?: string;
  backgroundPage?: ScadaPage | null;
  layers?: HMILayer[];
}>();
const emit = defineEmits<{
  (e: 'updateComponent', id: string, updates: Partial<HMIComponent>): void;
  (e: 'updatePage', updates: Partial<ScadaPage>): void;
  (e: 'collapse'): void;
}>();
```

分发采用映射表（示例）：
```ts
const widgetInspectorMap: Record<string, Component> = {
  'var-display': VarDisplayInspector,
  'button': ButtonInspector,
  'nav-menu': NavMenuInspector,
  'image': ImageInspector,
  'title-header': TitleHeaderInspector,
  'rounded-btn': RoundedButtonInspector,
  'sys-time': SysTimeInspector,
  'trend-chart': TrendChartInspector,
  'multi-var-dashboard': MultiVarDashboardInspector,
};
```
模板：
```vue
<div v-if="!selectedComponent && !backgroundPage"> …空态… </div>
<PageBackgroundInspector v-else-if="backgroundPage" :background-page="backgroundPage" :desktop-pages="desktopPages" :mobile-pages="mobilePages" :current-platform="currentPlatform" @update:page="(u)=>emit('updatePage',u)" />
<div v-else>
  <CommonWidgetSection :component="selectedComponent" :devices="devices" :layers="layers" :current-page-id="currentPageId" @update:component="(id,u)=>emit('updateComponent',id,u)" />
  <component :is="widgetInspectorMap[selectedComponent.type]" :component="selectedComponent" :devices="devices" :system-scripts="systemScripts" @update:component="(id,u)=>emit('updateComponent',id,u)" />
</div>
```

### 5.2 子组件通用契约

```ts
// 控件类子组件
const props = defineProps<{
  component: HMIComponent;
  devices: Device[];           // 来自 deviceStore（显式切片，D2）
  systemScripts?: SystemScript[]; // 按需
  // …其它所需 store 切片
}>();
const emit = defineEmits<{
  (e: 'update:component', id: string, updates: Partial<HMIComponent>): void;
  (e: 'update:page', updates: Partial<ScadaPage>): void;
}>();
// 子组件内 updateProp 等价实现：
const updateProp = (key: string, value: any) =>
  emit('update:component', props.component.id, { props: { ...props.component.props, [key]: value } });
```

`PageBackgroundInspector` 用 `backgroundPage: ScadaPage` + `emit('update:page', updates)`；
`CommonWidgetSection` 用 `component` + `devices` + `layers` + `currentPageId`，同样 `emit('update:component', …)`。

> **P1 缓解**：`CommonWidgetSection` 内现有 `['gauge-dial', …].includes(component.type)` 等类型数组条件**逐字照搬**当前模板（`InspectorPanel.vue:1203–1309`），由子组件读 `component.type` 复算，保证行为完全一致，不靠父级 prop 透传。

---

## 6. Before / After

**Before**（当前）
```
InspectorPanel.vue  (2681 行)
 ├─ <script setup> 547 行（所有控件逻辑堆在一起）
 └─ <template> 2133 行（单文件大 v-if 链）
```

**After**
```
InspectorPanel.vue            (~100 行壳)
inspector/
 ├─ CommonWidgetSection.vue   (公共区，~400 行)
 ├─ PageBackgroundInspector.vue (~300 行)
 ├─ RoundedButtonInspector.vue (~500 行)
 ├─ TrendChartInspector.vue    (~350 行)
 ├─ MultiVarDashboardInspector.vue (~450 行)
 ├─ NavMenuInspector.vue       (~180 行)
 ├─ VarDisplayInspector.vue    (~80 行)
 ├─ ButtonInspector.vue        (~80 行)
 ├─ ImageInspector.vue         (~60 行)
 ├─ TitleHeaderInspector.vue   (~120 行)
 └─ SysTimeInspector.vue       (~30 行)
```
（行数为估算，实际以拆分后为准；壳目标 < 120 行）

---

## 7. 各子组件职责与来源映射

| 子组件 | 职责 | 模板来源 | 脚本来源 |
|---|---|---|---|
| `CommonWidgetSection` | 布局/图层/PLC绑定/外观开关/状态文案/量程/阈值 | `886–1309` | `29–84`（按控件需要的辅助） |
| `PageBackgroundInspector` | 页面背景（纯色/渐变/图片）+ 自适应屏 + 5 套主题预设 + 背景图库 | `571–866` | `427–546`、`457–471` |
| `VarDisplayInspector` | 数据显示：小数位/可设定/写入范围/二次确认 | `1557–1614` | — |
| `ButtonInspector` | 导航/设值模式 + 复合绑定 | `1615–1662` | `171–204` |
| `NavMenuInspector` | 菜单项增删移 + 图标网格（含本地 `openIconPickerIndex`） | `1663–1784` | `205–242` |
| `ImageInspector` | 图元预览 + 换图库（含本地 `showImagePicker`） | `1785–1818` | `457–471` |
| `TitleHeaderInspector` | 三套风格配置 | `1819–1932` | — |
| `RoundedButtonInspector` | 预设/控制模式/脚本绑定/操作变量绑定/双态多态/圆角边框 | `1933–2191` | `86–170`、`125–138`、字体块 `2639` 区 |
| `SysTimeInspector` | 时钟格式 | `2192–2208` | — |
| `TrendChartInspector` | 多序列绑定/坐标轴/度量 + 旧式单绑定迁移 | `1310–1556` | `328–426` |
| `MultiVarDashboardInspector` | 看板排版/外框/多变量列表导入 | `2209–2638` | `243–327` |

---

## 8. 执行分批计划（按既定流程：写码 → 你 review → 增量 commit）

- **Phase 0**：本设计文档审批通过。
- **Phase 1（验证契约）**：`CommonWidgetSection` + 壳调度 + `PageBackgroundInspector` + `VarDisplayInspector` / `ImageInspector`（2 个简单件）。目的：打通「子组件 emit → 父转发 → 上游落库」链路，编译/类型检查通过。
- **Phase 2（三大块）**：`RoundedButtonInspector` + `TrendChartInspector` + `MultiVarDashboardInspector`。
- **Phase 3（收尾）**：`ButtonInspector` / `NavMenuInspector` / `TitleHeaderInspector` / `SysTimeInspector` + 字体块收尾。
- 每批完成后你 review，我增量 `git commit`（Structured Conventional Commits，partial-plan 标记）；默认不 push，需你确认。

---

## 9. 风险与缓解

| 级别 | 风险 | 缓解 |
|---|---|---|
| **P0** | 破坏 `updateComponent`/`updatePage` 单一桥 → 落库链路断裂 | 子组件只 `emit('update:component'/'update:page')`，父壳统一转发；不直连 store。 |
| **P1** | `CommonWidgetSection` 内类型数组条件（`['gauge-dial',…].includes(type)`）拆出后显隐错乱 | 条件逐字照搬当前模板，子组件读 `component.type` 复算，行为不变。 |
| **P2** | 本地 UI 状态（`openIconPickerIndex` 图标选择、`showImagePicker` 图片选择）遗留父级 | 随对应模板块下移至 `NavMenuInspector` / `ImageInspector` / `PageBackgroundInspector`。 |
| **P2** | store 切片 prop 遗漏导致子组件缺数据 | 按表 3.4 显式传入 `devices` / `systemScripts` / `desktopPages` 等；全局 store 仍可在子组件内 import 兜底。 |
| **P2** | 重构引入隐性行为差异 | 每批附手动烟雾测试清单（见 §10），逐控件比对。 |

---

## 10. 验收标准

1. `vite` 构建 + `vue-tsc` 类型检查通过。
2. `InspectorPanel.vue` 行数降至 **< 120 行**（仅壳）。
3. 逐控件手动烟雾测试（行为须与重构前一致）：
   - 空态显示；点击画布背景出现页面属性；背景纯色/渐变/图片切换 + 5 套主题预设套用；
   - 通用：布局/图层显隐锁定/PLC 绑定/外观边框背景开关/状态文案/量程/高·低限阈值；
   - `var-display` 小数位/可设定/二次确认；`button` 导航与设值；`nav-menu` 增删移 + 图标选择；
   - `image` 换图；`title-header` 三套风格；`rounded-btn` 预设/控制模式/脚本绑定/操作变量/双态多态；
   - `sys-time` 格式；`trend-chart` 多序列增删/颜色线宽/迁移；`multi-var-dashboard` 排版/外框/变量导入。
4. 无新增编译警告（保持项目既有 `<Nullable>enable</Nullable>` 等质量约束精神，前端同理避免 `any` 扩散）。
