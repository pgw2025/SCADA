# 趋势图坐标系增强设计方案（trend-chart 坐标轴 / 刻度 / 相对坐标 / 点位值）

> 状态：方案设计稿（仅设计，未改代码）。待用户确认决策点后落地。
> 关联：`docs/trend-chart-multiseries-design.md`（多变量序列改造，已提交 d07aa84）

## 1. 现状回顾

| 项 | 当前实现 | 位置 |
|---|---|---|
| Y 轴网格 | 3 条写死虚线（25/50/75%），无刻度数值 | `HMIWidget.vue:1207-1209` |
| Y 轴量程 | `chartSeries` 内按 `trendUseGlobalRange` + 各序列 `minValue/maxValue` 计算 | `HMIWidget.vue:733-782` |
| 坐标模式 | 仅绝对工程量值 | — |
| 手动范围 | 仅逐序列 `minValue/maxValue`（空则全局自适应），无图表级固定范围 | `HmiTrendSeries` `types.ts:48-49` |
| 点位值 | 仅图例区显示"当前值"，图上不显示每个点的值 | `HMIWidget.vue:1190-1197` |
| X 轴 | 无时间基准（缓冲仅 `number[]`，无时间戳） | `utils/trendHistory.ts` |
| 文字大小 | 仅图例字号 `trendLegendFontSize`（默认 9） | `types.ts:263` |

## 2. 目标能力（对应需求）

1. 坐标系刻度（X / Y 轴网格 + 数值标签）
2. 手动设置坐标范围（图表级固定 Y 范围，覆盖自适应）
3. 相对坐标 / 绝对坐标切换
4. 可开关"图形中每个点位的值"是否显示
5. 可设置显示文字大小（刻度字号 + 点位值字号）

## 3. 新增配置项总表

全部为 `trend-chart` 组件级 `props`（落在 `HMIComponent['props']`，非逐序列），以图表级统一控制坐标轴。

| 配置项 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `trendAxisMode` | `'absolute' \| 'relative'` | `'absolute'` | 绝对坐标=工程量真实值（带单位）；相对坐标=映射为量程区间百分比 0–100% |
| `trendAxisMin` | `number?` | `null` | 手动 Y 轴下限；与 `trendAxisMax` 同时有效时**固定**坐标范围 |
| `trendAxisMax` | `number?` | `null` | 手动 Y 轴上限 |
| `trendUseGlobalRange` | `boolean` | `true` | 自动量程模式下多序列是否共享量程（固定范围时此开关无意义，保持兼容） |
| `trendShowGrid` | `boolean` | `true` | 显示网格线 |
| `trendShowAxisLabels` | `boolean` | `true` | 显示坐标轴刻度数值 |
| `trendAxisLabelFontSize` | `number` | `8` | 刻度数值字号 (px) |
| `trendShowPointValues` | `boolean` | `false` | 在图形上显示每个点位数值 |
| `trendPointValueFontSize` | `number` | `8` | 点位数值字号 (px) |
| `trendPointValueColor` | `string \| 'auto'` | `'auto'` | 点位数值颜色；`auto`=取序列线条色 |
| `trendPointValueEveryN` | `number?` | `null` | 仅每 N 个点显示（null=自动抽稀，避免密集重叠） |

> 保留既有逐序列 `minValue/maxValue`/`precision`/`threshold` 语义：自动量程时仍用于"逐序列独立量程"；一旦设置图表级 `trendAxisMin/Max`，图表级固定范围优先。

## 4. Y 轴行为统一模型（核心）

`HMIWidget.vue` 新增 `trendAxis` computed，集中产出统一坐标范围与刻度，替代 `chartSeries` 内部各自算量程：

```
决定 niceMin / niceMax:
  ├─ trendAxisMin 与 trendAxisMax 均有限且 max>min  → 固定范围 = [axisMin, axisMax]   （手动范围，最高优先级）
  ├─ 否则若 trendUseGlobalRange=true               → 全局共享自适应（跨序列取 min/max + 10% 余量）
  └─ 否则                                          → 逐序列独立自适应（沿用现有行为）
坐标模式:
  ├─ absolute → 直接标工程量值 + 单位
  └─ relative → 值映射为 (v - niceMin)/(niceMax - niceMin) * 100，轴标 0%..100%
```

**刻度数值（Y）规则**：
- 存在**共享轴**（手动固定范围 / 相对模式 / 全局自适应）时，绘制 Y 刻度数值（用 nice-step 算法生成约 4–5 个"漂亮"刻度，如 0/25/50/75/100）。
- 仅当"绝对 + 逐序列独立"时，各序列按自身范围归一化，此时只画网格线、**不画共享数值标签**（避免数值歧义，保持现有行为）。

**X 轴刻度基准**（见决策点 D2）：
- 若缓冲改为存 `{t, v}` → X 标相对流逝时间（如 -30s / -20s / -10s / now）。
- 否则 → X 标采样序号（最旧←→最新），或仅画网格不标数值。

## 5. 渲染改动（HMIWidget.vue）

1. 几何调整：左侧预留 ~30px 给 Y 刻度标签，底部预留 ~14px 给 X 刻度标签（`innerW/innerH` 计算相应减去），顶部图例区不变。
2. 用 `trendAxis` computed 的 `yTicks`/`xTicks` 动态生成 `<line>` 网格 + `<text>` 刻度（替代写死的 3 条虚线）。
3. `chartSeries` 的 path 映射改用 `trendAxis` 产出的 `niceMin/niceMax`（含 relative 模式映射）。
4. 点位值：在每条 `<path>` 之外，对（抽稀后的）每个采样点额外渲染 `<text>`（位于点上方偏移），字号取 `trendPointValueFontSize`，颜色取 `trendPointValueColor`。
5. 新增 `niceTicks(min, max, count)` 工具函数（标准 nice-number 算法）置于 `utils/trendSeries.ts` 或新 `utils/axisTicks.ts`。

## 6. 属性面板改动（InspectorPanel.vue）

在趋势图编辑区（`InspectorPanel.vue:1310-1476`）新增"坐标轴与显示"分组：
- 坐标模式：绝对值 / 相对值（单选）
- 手动范围：`trendAxisMin` / `trendAxisMax` 数字输入（空=自动）
- 开关：显示网格 / 显示刻度 / 显示点位值
- 字号：`trendAxisLabelFontSize` / `trendPointValueFontSize` 数字或滑块
- 点位值颜色：取色器（auto 选项）
- （可选）点位值抽稀间隔 `trendPointValueEveryN`

## 7. 决策点（待用户确认）

- **D1 相对坐标语义**：推荐"映射为量程区间百分比 0–100%"。备选：相对某基准值偏移量 / 保持其他。
- **D2 X 轴刻度基准**：推荐缓冲存 `{t,v}` 以显示真实相对时间刻度（需小幅改 `trendHistory` 与 push 签名）；备选：标采样序号（不改缓冲）/ 不显示 X 数值。
- **D3 点位值密度**：推荐自动抽稀（点间距 < ~28px 时隔点显示，始终显示最新点）；备选：固定每 N 点 / 仅显示最新点。
- **D4 手动范围优先级**：推荐图表级固定范围覆盖逐序列 `minValue/maxValue`；备选：逐序列优先。

## 8. 验证建议

1. 设 `trendAxisMin/Max` → Y 轴固定范围、网格与数值同步更新。
2. 切 `relative` → 轴变 0–100%，多条不同量纲曲线可直接对比。
3. 开"显示点位值" → 图上每个点显示数值，密集时自动抽稀不重叠。
4. 调字号 → 刻度与点位值文字大小随之变化。
5. 旧图（仅 `trendSeries` 无新属性）→ 全部取默认值，行为不变（向后兼容）。

## 9. 决策结论（已与用户确认）

- **D1 相对坐标语义** = 映射为量程区间百分比 0–100%（`relative` 模式：`(v - axisMin)/(axisMax - axisMin) * 100`），Y 轴标 0%..100%，便于多量纲曲线同图对比。
- **D2 X 轴刻度基准** = 缓冲改为存 `{t:number, v:number}`，X 轴标相对流逝时间（如 `-30s / -20s / -10s / now`）；需同步改 `utils/trendHistory.ts` 缓冲结构与两处运行视图的 `pushTrendPoint` 签名。
- **D3 点位值密度** = 自动抽稀（点间距 < ~28px 时隔点显示标签，始终显示最新点的值）。
- **D4 手动范围优先级** = 图表级固定范围（`trendAxisMin/Max`）覆盖逐序列 `minValue/maxValue`；坐标轴单一清晰。

> 落地范围（待用户"修改代码"指令后执行）：`types.ts`（新增 props）、`widgetRegistry.ts`（默认值）、`utils/trendHistory.ts`（{t,v} 缓冲）、`utils/axisTicks.ts`（新增 nice-step）、`HMIWidget.vue`（`trendAxis` computed + 动态网格/刻度 + 点位值渲染 + 几何留白）、`ScadaTopologyView.vue`/`ScadaPlayerView.vue`（push 携带 t）、`InspectorPanel.vue`（坐标轴与显示分组编辑器）。

## 10. 实现落地（已完成，未构建未 push）

- `types.ts`：新增 10 个趋势坐标轴 props（`trendAxisMode` / `trendAxisMin/Max` / `trendShowGrid` / `trendShowAxisLabels` / `trendAxisLabelFontSize` / `trendShowPointValues` / `trendPointValueFontSize` / `trendPointValueColor` / `trendPointValueEveryN`）。
- `widgetRegistry.ts`：`trend-chart` 默认值补齐上述字段。
- `utils/trendHistory.ts`：缓冲结构改为 `Record<cid, Record<sid, TrendSample[]>>`（TrendSample={t,v}），`pushTrendPoint` 增可选 `timestamp`（缺省 `Date.now()`），保持 `reactive` 响应式与 `MAX_POINTS` 截断。运行态两视图调用签名向后兼容（无需改）。
- `utils/axisTicks.ts`（新增）：`niceTicks`（漂亮刻度）、`relTimeLabel`（相对时间）、`fmtTick`（刻度格式化）。
- `HMIWidget.vue`：用 `trendChart` computed 统一产出共享轴范围 / Y/X 刻度 / 逐序列 path 与点位值标签；X 以时间戳定位（真实相对时间刻度），无时间跨度回退等距；点位值自动抽稀（间距<28px 隔点、始终保留最新点）；几何左/下留白给刻度。
- `InspectorPanel.vue`：趋势图编辑区新增"坐标轴与显示"分组（坐标模式 / 手动范围 / 网格·刻度·点位值开关 / 刻度与点位值字号 / 点位值颜色）。

共享轴优先级：**手动范围 > 相对模式(0-100%) > 全局自适应 > 逐序列独立**。旧趋势图无新属性时全部取默认值，行为不变（向后兼容）。
