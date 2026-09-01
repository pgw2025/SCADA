<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';
import { getEffectiveTrendSeries } from '../../utils/trendSeries';
import { niceTicks, relTimeLabel, fmtTick } from '../../utils/axisTicks';
import type { TrendSample } from '../../utils/trendHistory';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const trendSeriesList = computed(() => getEffectiveTrendSeries(props.component));
const trendShowLegend = computed(() => propOr('trendShowLegend', true));
const trendLegendFontSize = computed(() => Number(propOr('trendLegendFontSize', 9)));
const hasTrendData = computed(() => trendSeriesList.value.length > 0);
// 是否任一序列已采到 ≥2 个真实采样点可绘制
const trendReady = computed(() => {
  const map = props.history ?? {};
  return Object.values(map).some((buf) => (buf?.length ?? 0) >= 2);
});

const numOrNull = (k: string): number | null => {
  const v = props.component.props[k as keyof HMIComponent['props']];
  return (v === undefined || v === null || v === '') ? null : Number(v);
};
const trendAxisMode = computed(() => (propOr('trendAxisMode', 'absolute') === 'relative' ? 'relative' : 'absolute'));
const manualAxisMin = computed(() => numOrNull('trendAxisMin'));
const manualAxisMax = computed(() => numOrNull('trendAxisMax'));
const useGlobalRange = computed(() => propOr('trendUseGlobalRange', true));
const showGrid = computed(() => propOr('trendShowGrid', true) === true);
const showAxisLabels = computed(() => propOr('trendShowAxisLabels', true) === true);
const axisLabelFontSize = computed(() => Number(propOr('trendAxisLabelFontSize', 8)));
const showPointValues = computed(() => propOr('trendShowPointValues', false) === true);
const pointValueFontSize = computed(() => Number(propOr('trendPointValueFontSize', 8)));
const pointValueColor = computed(() => propOr('trendPointValueColor', 'auto'));
const pointEveryN = computed(() => numOrNull('trendPointValueEveryN'));

const trendChart = computed(() => {
  const series = trendSeriesList.value;
  const map = (props.history ?? {}) as Record<string, TrendSample[]>;
  const W = width.value, H = height.value;
  const padL = 32, padR = 8, padT = 6, padB = 16; // 左留 Y 刻度，下留 X 刻度
  const innerW = Math.max(1, W - padL - padR);
  const innerH = Math.max(1, H - padT - padB);
  const left = padL, top = padT;

  // 共享轴参考范围 mapLo/mapHi（数值归一化用）；yTickVals 为刻度数值
  let mapLo = 0, mapHi = 1, hasShared = false, yTickVals: number[] = [];
  const mMin = manualAxisMin.value, mMax = manualAxisMax.value;
  const isRel = trendAxisMode.value === 'relative';

  if (mMin != null && mMax != null && mMax > mMin) {
    // 手动固定范围（图表级覆盖逐序列 minValue/maxValue）
    mapLo = mMin; mapHi = mMax; hasShared = true;
    yTickVals = niceTicks(mMin, mMax, 4);
  } else if (isRel) {
    // 相对坐标：以全局数据范围作参考，轴标 0-100%
    let rMin = Infinity, rMax = -Infinity;
    series.forEach((s) => { const buf = map[s.id] ?? []; if (buf.length) { const vs = buf.map(p => p.v); rMin = Math.min(rMin, ...vs); rMax = Math.max(rMax, ...vs); } });
    if (!Number.isFinite(rMin)) { rMin = 0; rMax = 100; } else if (rMax <= rMin) { rMax = rMin + 1; }
    mapLo = rMin; mapHi = rMax; hasShared = true;
    yTickVals = niceTicks(0, 100, 4);
  } else if (useGlobalRange.value) {
    // 绝对 + 全局共享自适应
    let gMin = Infinity, gMax = -Infinity;
    series.forEach((s) => {
      const buf = map[s.id] ?? [];
      const lo = Number(s.minValue), hi = Number(s.maxValue);
      if (Number.isFinite(lo) && Number.isFinite(hi) && hi > lo) { gMin = Math.min(gMin, lo); gMax = Math.max(gMax, hi); }
      else if (buf.length) { const vs = buf.map(p => p.v); gMin = Math.min(gMin, ...vs); gMax = Math.max(gMax, ...vs); }
    });
    if (!Number.isFinite(gMin) || !Number.isFinite(gMax) || gMax <= gMin) { gMin = 0; gMax = 100; }
    else { const m = (gMax - gMin) * 0.1 || 1; gMin -= m; gMax += m; }
    mapLo = gMin; mapHi = gMax; hasShared = true;
    yTickVals = niceTicks(gMin, gMax, 4);
  }

  const yTicks = yTickVals.map((v) => {
    const r = (mapHi - mapLo) || 1;
    const ratio = Math.max(0, Math.min(1, (v - mapLo) / r));
    return { value: v, y: top + (innerH - ratio * innerH) };
  });
  // 无共享轴（绝对 + 逐序列独立）时仍画 3 条默认网格线
  const grid: { y: number; label?: string }[] = hasShared
    ? yTicks.map((t) => ({ y: t.y, label: fmtTick(t.value) + (isRel ? '%' : '') }))
    : [0.25, 0.5, 0.75].map((f) => ({ y: top + innerH - f * innerH }));

  // 可见窗口（最多 innerW/6 个采样，按时间定位铺满整宽）
  const wins = series.map((s) => {
    const buf = map[s.id] ?? [];
    const window = buf.slice(-Math.max(2, Math.floor(innerW / 6)));
    return { s, buf, window };
  });

  // X 时间基准：以「可见窗口」的联合时间范围为轴。
  // 修复：此前用全量缓冲的 tOldest/span 作基准，而只绘制最近 innerW/6 个采样，
  // 导致这些点都被映射到靠近右边缘，曲线被压缩、X 轴 0 点（最旧可见采样）不在画面内。
  // 现改为基于可见窗口自身时间范围，最旧采样落在 left（X 轴原点），曲线铺满整宽。
  let winTMin = Infinity, winTMax = -Infinity;
  for (const w of wins) for (const p of w.window) {
    if (p.t < winTMin) winTMin = p.t;
    if (p.t > winTMax) winTMax = p.t;
  }
  const winSpan = Number.isFinite(winTMin) && winTMax > winTMin ? winTMax - winTMin : 0;
  const nowMs = Number.isFinite(winTMax) ? winTMax : Date.now();

  const xTicks: { x: number; label: string }[] = [];
  if (showAxisLabels.value && winSpan > 0) {
    const N = 4;
    for (let i = 0; i <= N; i++) {
      const frac = i / N;
      xTicks.push({ x: left + frac * innerW, label: relTimeLabel(winTMin + winSpan * frac, nowMs) });
    }
  }

  const seriesOut = wins.map(({ s, buf, window }) => {
    let lo = mapLo, hi = mapHi;
    if (!hasShared) {
      const loS = Number(s.minValue), hiS = Number(s.maxValue);
      if (Number.isFinite(loS) && Number.isFinite(hiS) && hiS > loS) { lo = loS; hi = hiS; }
      else if (buf.length) { const vs = buf.map(p => p.v); lo = Math.min(...vs); hi = Math.max(...vs); if (hi <= lo) hi = lo + 1; else { const m = (hi - lo) * 0.1 || 1; lo -= m; hi += m; } }
    }
    const wlen = window.length;
    const xOf = (p: TrendSample) => {
      if (winSpan > 0 && wlen > 1) return left + Math.max(0, Math.min(1, (p.t - winTMin) / winSpan)) * innerW;
      const idx = window.indexOf(p);
      return left + (wlen <= 1 ? innerW : (idx / (wlen - 1)) * innerW);
    };
    const yNorm = (v: number) => {
      const r = (hi - lo) || 1;
      const ratio = Math.max(0, Math.min(1, (v - lo) / r));
      return top + (innerH - ratio * innerH);
    };
    let d = '';
    window.forEach((p, i) => { const x = xOf(p); const y = yNorm(p.v); d += `${i === 0 ? 'M' : ' L'} ${x.toFixed(1)} ${y.toFixed(1)}`; });

    const current = buf.length ? buf[buf.length - 1].v : 0;
    const alert = (s.thresholdMax != null && current >= s.thresholdMax) ? 'high'
      : (s.thresholdMin != null && current <= s.thresholdMin) ? 'low' : null;
    const color = alert === 'high' ? '#ef4444' : alert === 'low' ? '#f59e0b' : (s.color || '#10b981');
    const label = s.label?.trim() || s.variableKey || '变量';
    const unit = s.unit || '';
    const prec = (s.precision != null && s.precision >= 0) ? s.precision : 1;

    const pts: { x: number; y: number; text: string }[] = [];
    if (showPointValues.value && wlen > 0) {
      const spacing = wlen > 1 ? innerW / (wlen - 1) : innerW;
      const autoStep = spacing > 0 ? Math.max(1, Math.ceil(28 / spacing)) : 1;
      const dec = Math.max(1, pointEveryN.value ?? autoStep);
      window.forEach((p, i) => {
        if (i % dec !== 0 && i !== wlen - 1) return; // 始终保留最新点
        pts.push({ x: xOf(p), y: yNorm(p.v) - 6, text: p.v.toFixed(prec) + (unit ? ' ' + unit : '') });
      });
    }
    return { id: s.id, d, color, lineWidth: s.lineWidth || 2, label, current, unit, points: pts };
  });

  return {
    left, top, innerW, innerH, padB, hasShared, grid, xTicks, series: seriesOut, isRel,
    showGrid: showGrid.value, showAxisLabels: showAxisLabels.value,
    axisLabelFontSize: axisLabelFontSize.value, pointColor: pointValueColor.value, pointFontSize: pointValueFontSize.value,
  };
});

// 图例数值格式化（保留 1 位小数）
const trendValFmt = (v: number) => (typeof v === 'number' ? v.toFixed(1) : `${v}`);
</script>

<template>
<div
      class="w-full h-full bg-slate-950 border border-slate-800 rounded-lg p-1.5 font-mono text-slate-400 flex flex-col">
      <div class="flex items-center justify-between mb-1 border-b border-slate-800 pb-1 gap-2">
        <span class="font-bold text-slate-300 truncate text-[9px]">{{ component.label || component.name || '实时趋势' }}</span>
        <div v-if="trendShowLegend && hasTrendData" class="flex flex-col items-end gap-0.5 min-w-0"
          :style="{ fontSize: trendLegendFontSize + 'px' }">
          <div v-for="s in trendChart.series" :key="s.id" class="flex items-center gap-1 leading-none">
            <span class="w-2 h-0.5 rounded-full" :style="{ background: s.color }" />
            <span class="truncate max-w-[90px] text-slate-300">{{ s.label }}</span>
            <span class="font-bold text-slate-100">{{ trendValFmt(s.current) }}<template v-if="s.unit"> {{ s.unit }}</template></span>
          </div>
        </div>
      </div>
      <!-- 占位：未绑定数据源或采样点不足时不绘制伪造曲线 -->
      <div v-if="!trendReady" class="flex-1 flex flex-col items-center justify-center gap-1 text-slate-500">
        <span class="w-1.5 h-1.5 rounded-full bg-slate-600 animate-pulse" />
        <span class="text-[9px]">{{ hasTrendData ? '等待采样…' : '暂无数据' }}</span>
        <span class="text-[8px] text-slate-600">{{ hasTrendData ? '采集 ≥2 点后自动绘制' : '请在编辑器中绑定变量/序列' }}</span>
      </div>
      <div v-else class="flex-1 relative">
        <svg width="100%" height="100%">
          <!-- 网格线 + Y 轴刻度数值 -->
          <template v-if="trendChart.showGrid || trendChart.showAxisLabels">
            <g v-for="(gl, i) in trendChart.grid" :key="'g' + i">
              <line v-if="trendChart.showGrid" :x1="trendChart.left" :y1="gl.y" :x2="trendChart.left + trendChart.innerW" :y2="gl.y"
                stroke="#334155" stroke-width="0.5" stroke-dasharray="3" />
              <text v-if="trendChart.showAxisLabels && gl.label" :x="trendChart.left - 3" :y="gl.y + 3" text-anchor="end"
                :font-size="trendChart.axisLabelFontSize" fill="#64748b">{{ gl.label }}</text>
            </g>
            <!-- X 轴相对时间刻度 -->
            <g v-for="(xt, i) in trendChart.xTicks" :key="'x' + i">
              <line v-if="trendChart.showGrid" :x1="xt.x" :y1="trendChart.top" :x2="xt.x" :y2="trendChart.top + trendChart.innerH"
                stroke="#334155" stroke-width="0.5" stroke-dasharray="3" />
              <text :x="xt.x" :y="trendChart.top + trendChart.innerH + 11" text-anchor="middle"
                :font-size="trendChart.axisLabelFontSize" fill="#64748b">{{ xt.label }}</text>
            </g>
          </template>
          <!-- 序列线条 -->
          <path v-for="s in trendChart.series" :key="s.id" :d="s.d" fill="none" :stroke="s.color"
            :stroke-width="s.lineWidth" stroke-linecap="round" stroke-linejoin="round" />
          <!-- 点位值标签（自动抽稀，始终保留最新点） -->
          <g v-for="(s, si) in trendChart.series" :key="'pv' + si">
            <text v-for="(pt, pi) in s.points" :key="pi" :x="pt.x" :y="pt.y" text-anchor="middle"
              :font-size="trendChart.pointFontSize" :fill="trendChart.pointColor === 'auto' ? s.color : trendChart.pointColor">{{ pt.text }}</text>
          </g>
        </svg>
      </div>
    </div>
</template>
