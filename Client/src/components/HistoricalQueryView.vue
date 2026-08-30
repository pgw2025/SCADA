<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { historicalRecords } from '../store/historyStore';
import { systemConfig, addLog } from '../store/index';
import {
  fetchHistoryFromBackend,
  fetchHistoryBatch,
  exportHistoryCsv
} from '../api/historyApi';
import { fetchDevicesFromBackend } from '../api/deviceApi';
import { normalizeDevices } from '../utils/deviceStatus';
import { HistoryVariableOption, HistoricalRecord } from '../types';
import {
  Search,
  Calendar,
  TrendingUp,
  ChevronLeft,
  ChevronRight,
  AlertCircle,
  FileSpreadsheet,
  X,
  Loader2
} from 'lucide-vue-next';

// ==================== 常量 ====================
const MAX_SELECTED = 6;            // 多变量对比上限（D2）
const CHART_TARGET_POINTS = 600;   // LTTB 降采样目标点数
const SVG_W = 800;
const SVG_H = 240;
const PAD_X = 50;
const PAD_Y = 30;
const CHART_COLORS = ['#38bdf8', '#34d399', '#f59e0b', '#f472b6', '#a78bfa', '#f87171'];
const PAGE_SIZE = 15;

const AGG_OPTIONS = [
  { label: '原始数据', value: 0 },
  { label: '1分钟', value: 60 * 1000 },
  { label: '5分钟', value: 5 * 60 * 1000 },
  { label: '1小时', value: 60 * 60 * 1000 },
  { label: '1天', value: 24 * 60 * 60 * 1000 }
];
const AGG_FN_OPTIONS = [
  { label: '均值 mean', value: 'mean' },
  { label: '最大 max', value: 'max' },
  { label: '最小 min', value: 'min' },
  { label: '首值 first', value: 'first' },
  { label: '末值 last', value: 'last' }
];

type TimeframeKey = 'hour' | 'day' | 'three_days' | 'month' | 'all' | 'custom';
const TIMEFRAME_OPTIONS: { key: TimeframeKey; label: string; spanMs: number }[] = [
  { key: 'hour', label: '最近1小时', spanMs: 3600 * 1000 },
  { key: 'day', label: '最近1天', spanMs: 24 * 3600 * 1000 },
  { key: 'three_days', label: '最近3天', spanMs: 3 * 24 * 3600 * 1000 },
  { key: 'month', label: '最近1月', spanMs: 30 * 24 * 3600 * 1000 },
  { key: 'all', label: '所有数据', spanMs: 0 },
  { key: 'custom', label: '自定义', spanMs: -1 }
];

// ==================== 类型 ====================
interface SelectedVar {
  deviceKey: string;
  variableKey: string;
  deviceName: string;
  variableName: string;
  unit?: string;
}
interface HistorySeries {
  key: string;
  deviceKey: string;
  variableKey: string;
  variableName: string;
  deviceName: string;
  unit?: string;
  color: string;
  records: HistoricalRecord[];
}
interface ChartPoint { t: number; v: number; bad: boolean; }

// ==================== 模拟模式演示变量（后端不可用时回退） ====================
const demoVariables: HistoryVariableOption[] = [
  { deviceId: 0, deviceKey: 'demo_water_treatment', deviceName: '演示·水处理', variableKey: 'tank_level', variableName: '核心储水罐液位指标', unit: '%' },
  { deviceId: 0, deviceKey: 'demo_water_treatment', deviceName: '演示·水处理', variableKey: 'purified_level', variableName: '二级过滤沉淀池水位', unit: '%' },
  { deviceId: 0, deviceKey: 'demo_water_treatment', deviceName: '演示·水处理', variableKey: 'flow_rate', variableName: '总干线多相流瞬时流阻', unit: 'm³/h' },
  { deviceId: 0, deviceKey: 'demo_thermal_plant', deviceName: '演示·热电厂', variableKey: 'boiler_temp', variableName: '热能锅炉受热壁核心温度', unit: '℃' },
  { deviceId: 0, deviceKey: 'demo_thermal_plant', deviceName: '演示·热电厂', variableKey: 'boiler_press', variableName: '汽包炉高压阻抗安全压力', unit: 'kPa' },
  { deviceId: 0, deviceKey: 'demo_material_line', deviceName: '演示·物料线', variableKey: 'conveyor_speed', variableName: '物料流至分拣轮转速设定', unit: 'rpm' }
];

// ==================== 数据源（真实模式动态加载） ====================
const dynamicVariables = ref<HistoryVariableOption[]>([]);
const isSimulation = computed(() => systemConfig.value.isSimulationActive);
const selectableVariables = computed<HistoryVariableOption[]>(() =>
  isSimulation.value ? demoVariables : dynamicVariables.value
);

// ==================== 查询状态 ====================
const searchInput = ref('');
const isInputFocused = ref(false);
// blur 延迟收起下拉：留出点击选项的响应窗口（模板作用域无 window，须在 script 定义）
const onSearchBlur = () => {
  setTimeout(() => { isInputFocused.value = false; }, 250);
};
const selectedVars = ref<SelectedVar[]>([]);
const selectedTimeframe = ref<TimeframeKey>('all');
const customStart = ref('');
const customEnd = ref('');
const aggregateWindowMs = ref<number>(0);
const aggregateFn = ref<string>('mean');
const currentPageNum = ref(1);
const isLoading = ref(false);
const seriesList = ref<HistorySeries[]>([]);
const visibleKeys = ref<Record<string, boolean>>({});
const tooltip = ref<{ x: number; y: number; time: string; items: { color: string; label: string; value: string; bad: boolean }[] } | null>(null);

const seriesKey = (deviceKey: string, variableKey: string) => `${deviceKey}|${variableKey}`;

// ==================== 变量下拉 ====================
const filteredDropdownOptions = computed(() => {
  const query = searchInput.value.toLowerCase().trim();
  if (!query) return selectableVariables.value;
  return selectableVariables.value.filter(v =>
    v.deviceName.toLowerCase().includes(query) ||
    v.variableName.toLowerCase().includes(query) ||
    v.variableKey.toLowerCase().includes(query)
  );
});

const handleSelectVariable = (o: HistoryVariableOption) => {
  const key = seriesKey(o.deviceKey, o.variableKey);
  const exists = selectedVars.value.some(v => seriesKey(v.deviceKey, v.variableKey) === key);
  if (exists) {
    selectedVars.value = selectedVars.value.filter(v => seriesKey(v.deviceKey, v.variableKey) !== key);
  } else {
    if (selectedVars.value.length >= MAX_SELECTED) {
      addLog('历史查询', `最多同时对比 ${MAX_SELECTED} 条曲线`, 'warning');
      return;
    }
    selectedVars.value = [
      ...selectedVars.value,
      { deviceKey: o.deviceKey, variableKey: o.variableKey, deviceName: o.deviceName, variableName: o.variableName, unit: o.unit }
    ];
  }
  isInputFocused.value = false;
  searchInput.value = '';
  executeHistoryQuery();
};

const handleRemoveSelected = (key: string) => {
  selectedVars.value = selectedVars.value.filter(v => seriesKey(v.deviceKey, v.variableKey) !== key);
  executeHistoryQuery();
};

// ==================== 时间范围 / 聚合 ====================
const currentSpanMs = computed(() => {
  const tf = TIMEFRAME_OPTIONS.find(t => t.key === selectedTimeframe.value)!;
  if (tf.key === 'custom') {
    if (customStart.value && customEnd.value) {
      const s = new Date(customStart.value).getTime();
      const e = new Date(customEnd.value).getTime();
      if (e > s) return e - s;
    }
    return 0;
  }
  return tf.spanMs;
});

const timeframeToRange = (): { start?: string; end?: string } => {
  const now = Date.now();
  switch (selectedTimeframe.value) {
    case 'hour': return { start: new Date(now - 3600 * 1000).toISOString() };
    case 'day': return { start: new Date(now - 24 * 3600 * 1000).toISOString() };
    case 'three_days': return { start: new Date(now - 3 * 24 * 3600 * 1000).toISOString() };
    case 'month': return { start: new Date(now - 30 * 24 * 3600 * 1000).toISOString() };
    case 'all':
      // 必须显式传极早起点，规避后端未传 start 时的 -30d 默认范围
      return { start: '2000-01-01T00:00:00Z' };
    case 'custom': {
      if (customStart.value && customEnd.value) {
        const s = new Date(customStart.value).getTime();
        const e = new Date(customEnd.value).getTime();
        if (e > s) return { start: new Date(s).toISOString(), end: new Date(e).toISOString() };
      }
      return {};
    }
    default: return {};
  }
};

const recommendAggregation = (): number => {
  const span = currentSpanMs.value;
  if (span <= 0 || span <= 6 * 3600 * 1000) return 0;
  if (span <= 3 * 24 * 3600 * 1000) return 5 * 60 * 1000;
  if (span <= 30 * 24 * 3600 * 1000) return 60 * 60 * 1000;
  return 24 * 60 * 60 * 1000;
};

const selectTimeframe = (key: TimeframeKey) => {
  selectedTimeframe.value = key;
  if (key !== 'custom') aggregateWindowMs.value = recommendAggregation();
  currentPageNum.value = 1;
  executeHistoryQuery();
};

const onCustomRangeChange = () => {
  aggregateWindowMs.value = recommendAggregation();
  currentPageNum.value = 1;
  executeHistoryQuery();
};

const onAggregationChange = () => {
  currentPageNum.value = 1;
  executeHistoryQuery();
};

// ==================== 查询执行 ====================
interface SeriesInput {
  deviceKey: string;
  variableKey: string;
  variableName: string;
  deviceName: string;
  unit?: string;
  records: HistoricalRecord[];
}

const executeHistoryQuery = async () => {
  if (selectedVars.value.length === 0) {
    seriesList.value = [];
    visibleKeys.value = {};
    return;
  }

  const range = timeframeToRange();
  isLoading.value = true;
  try {
    let merged: SeriesInput[];
    if (selectedVars.value.length === 1) {
      const v = selectedVars.value[0];
      const records = await fetchHistoryFromBackend({
        deviceKey: v.deviceKey,
        variableKey: v.variableKey,
        limit: 2000,
        start: range.start,
        end: range.end,
        aggregateWindowMs: aggregateWindowMs.value,
        aggregateFn: aggregateFn.value
      });
      merged = [{ deviceKey: v.deviceKey, variableKey: v.variableKey, variableName: v.variableName, deviceName: v.deviceName, unit: v.unit, records }];
    } else {
      const items = await fetchHistoryBatch({
        variables: selectedVars.value.map(v => ({ deviceKey: v.deviceKey, variableKey: v.variableKey })),
        limit: 2000,
        start: range.start,
        end: range.end,
        aggregateWindowMs: aggregateWindowMs.value,
        aggregateFn: aggregateFn.value
      });
      // 按选中顺序映射，保证曲线颜色稳定
      merged = selectedVars.value.map(v => {
        const it = items.find(i => i.deviceKey === v.deviceKey && i.variableKey === v.variableKey);
        return {
          deviceKey: v.deviceKey,
          variableKey: v.variableKey,
          variableName: it?.variableName || v.variableName,
          deviceName: v.deviceName,
          unit: v.unit,
          records: it?.records || []
        };
      });
    }
    applySeries(merged);
  } catch (err) {
    // 查询失败：清空序列，避免残留旧数据被误认为当前结果
    seriesList.value = [];
    visibleKeys.value = {};
  } finally {
    isLoading.value = false;
  }
};

const applySeries = (input: SeriesInput[]) => {
  seriesList.value = input.map((s, idx) => ({
    ...s,
    key: seriesKey(s.deviceKey, s.variableKey),
    color: CHART_COLORS[idx % CHART_COLORS.length]
  }));
  const vis: Record<string, boolean> = {};
  seriesList.value.forEach(s => (vis[s.key] = true));
  visibleKeys.value = vis;
  currentPageNum.value = 1;
};

// ==================== 数据加载 ====================
const loadVariableOptions = async () => {
  if (isSimulation.value) return;
  try {
    const { data } = await fetchDevicesFromBackend();
    const normalized = normalizeDevices(data, []);
    const opts: HistoryVariableOption[] = [];
    normalized.forEach(dev => {
      const meta = dev.variableMeta ?? {};
      Object.values(meta).forEach((v: any) => {
        if (!v || !v.key) return;
        opts.push({
          deviceId: dev.id,
          deviceKey: dev.key,
          deviceName: dev.name,
          variableKey: v.key,
          variableName: v.name || v.key,
          unit: v.unit || undefined
        });
      });
    });
    dynamicVariables.value = opts;
    // 默认选中第一个可用变量
    if (opts.length > 0 && selectedVars.value.length === 0) {
      handleSelectVariable(opts[0]);
    }
  } catch (err: any) {
    addLog('历史查询', `加载设备变量列表失败: ${err.message}（已回退演示变量）`, 'warning');
    dynamicVariables.value = [];
  }
};

onMounted(() => {
  if (isSimulation.value) {
    handleSelectVariable(demoVariables[0]);
  } else {
    loadVariableOptions();
  }
});

// ==================== 趋势图几何 ====================
const visibleSeries = computed(() => seriesList.value.filter(s => visibleKeys.value[s.key] !== false));

const toggleSeriesVisible = (key: string) => {
  visibleKeys.value = { ...visibleKeys.value, [key]: visibleKeys.value[key] === false };
};

const lttb = (points: ChartPoint[], threshold: number): ChartPoint[] => {
  if (points.length <= threshold || threshold < 3) return points;
  const sampled: ChartPoint[] = [points[0]];
  const every = (points.length - 2) / (threshold - 2);
  let a = 0;
  const avg = (s: number, e: number) => {
    let x = 0, y = 0, n = 0;
    for (let i = s; i < e; i++) { x += points[i].t; y += points[i].v; n++; }
    return { t: x / n, v: y / n };
  };
  for (let i = 0; i < threshold - 2; i++) {
    const rangeStart = Math.floor((i + 1) * every) + 1;
    const rangeEnd = Math.min(Math.floor((i + 2) * every) + 1, points.length);
    const avgEnd = rangeEnd < rangeStart ? rangeStart + 1 : rangeEnd;
    const avgPt = avg(rangeStart, avgEnd);
    const rangeOffs = Math.floor(i * every) + 1;
    const rangeTo = Math.floor((i + 1) * every) + 1;
    const ax = points[a].t;
    const ay = points[a].v;
    let maxArea = -1;
    let nextA = rangeOffs;
    for (let j = rangeOffs; j < rangeTo; j++) {
      const area = Math.abs((ax - avgPt.t) * (points[j].v - ay) - (ax - points[j].t) * (avgPt.v - ay)) * 0.5;
      if (area > maxArea) { maxArea = area; nextA = j; }
    }
    sampled.push(points[nextA]);
    a = nextA;
  }
  sampled.push(points[points.length - 1]);
  return sampled;
};

const formatTimeLabel = (t: number): string => {
  const d = new Date(t);
  const span = currentSpanMs.value;
  if (span > 0 && span <= 24 * 3600 * 1000) {
    return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  }
  if (span > 24 * 3600 * 1000 && span <= 31 * 24 * 3600 * 1000) {
    return `${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  }
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

const chartGeometry = computed(() => {
  // 各可见序列原始点（按时间升序）
  const ds = visibleSeries.value.map(s => {
    const pts: ChartPoint[] = s.records
      .filter(r => !isNaN(new Date(r.timestamp).getTime()))
      .map(r => ({ t: new Date(r.timestamp).getTime(), v: Number(r.value), bad: r.quality != null && r.quality !== 'Good' }))
      .sort((a, b) => a.t - b.t);
    return { key: s.key, color: s.color, unit: s.unit, label: s.variableName, pts };
  });

  // 全局时间域
  let tMin = Infinity, tMax = -Infinity;
  ds.forEach(d => d.pts.forEach(p => {
    if (p.t < tMin) tMin = p.t;
    if (p.t > tMax) tMax = p.t;
  }));
  if (!isFinite(tMin) || ds.length === 0) {
    return { series: [] as any[], yTicks: [] as any[], xTicks: [] as any[], multi: false, empty: true };
  }
  if (tMin === tMax) { tMin -= 1; tMax += 1; }
  const tSpan = tMax - tMin;
  const getX = (t: number) => PAD_X + ((t - tMin) / tSpan) * (SVG_W - 2 * PAD_X);
  const getYForValue = (v: number, min: number, max: number) => {
    const span = max - min || 1;
    return SVG_H - PAD_Y - ((v - min) / span) * (SVG_H - 2 * PAD_Y);
  };

  const multi = ds.length > 1;

  // 中位间隔：用于数据缺口断线（间隔 > 2×中位间隔 视为断点）
  const buildPath = (pts: ChartPoint[], norm: (v: number) => number): string => {
    if (pts.length < 2) return '';
    const gaps: number[] = [];
    for (let i = 1; i < pts.length; i++) gaps.push(pts[i].t - pts[i - 1].t);
    gaps.sort((a, b) => a - b);
    const medianGap = gaps[Math.floor(gaps.length / 2)] || 1;
    const threshold = medianGap * 2;
    let d = `M ${getX(pts[0].t)} ${norm(pts[0].v)}`;
    for (let i = 1; i < pts.length; i++) {
      const x2 = getX(pts[i].t);
      const y2 = norm(pts[i].v);
      if (pts[i].t - pts[i - 1].t > threshold) {
        d += ` M ${x2} ${y2}`;
      } else {
        const x1 = getX(pts[i - 1].t);
        const y1 = norm(pts[i - 1].v);
        const cpX1 = x1 + (x2 - x1) / 3;
        const cpX2 = x2 - (x2 - x1) / 3;
        d += ` C ${cpX1} ${y1}, ${cpX2} ${y2}, ${x2} ${y2}`;
      }
    }
    return d;
  };

  const series = ds.map((d, idx) => {
    const fullVals = d.pts.map(p => p.v);
    let min = fullVals.length ? Math.min(...fullVals) : 0;
    let max = fullVals.length ? Math.max(...fullVals) : 0;
    if (min === max) { min -= 1; max += 1; }
    if (!multi) {
      // 单曲线：沿用 0.9/1.1 留白（与原实现一致）
      min = Math.max(0, min * 0.9);
      max = max * 1.1;
    }
    const norm = (v: number) => getYForValue(v, min, max);
    const path = buildPath(d.pts, norm);
    const sampled = lttb(d.pts, CHART_TARGET_POINTS);
    const circleStep = Math.max(1, Math.floor(sampled.length / 12));
    const circles = sampled
      .filter((_, i) => i % circleStep === 0 || i === sampled.length - 1)
      .map(p => ({ x: getX(p.t), y: norm(p.v), v: p.v, t: p.t, bad: p.bad }));
    return { key: d.key, color: d.color, label: d.label, unit: d.unit, path, circles, min, max, norm };
  });

  // Y 轴刻度：多曲线归一化为百分比；单曲线显示数值
  let yTicks: { y: number; label: string }[] = [];
  if (multi) {
    yTicks = [0, 0.25, 0.5, 0.75, 1].map(f => ({
      y: SVG_H - PAD_Y - f * (SVG_H - 2 * PAD_Y),
      label: `${Math.round(f * 100)}%`
    }));
  } else if (series.length === 1) {
    const s = series[0];
    yTicks = [0, 1, 2, 3, 4].map(i => {
      const val = s.min + ((s.max - s.min) * i) / 4;
      return { y: s.norm(val), label: val.toFixed(1) };
    });
  }

  // X 轴刻度：5 等分时间标签
  const xTicks = [0, 1, 2, 3, 4].map(i => {
    const t = tMin + (tSpan * i) / 4;
    return { x: getX(t), label: formatTimeLabel(t) };
  });

  return { series, yTicks, xTicks, multi, empty: false, getX };
});

// ==================== 统计条 ====================
const statsBySeries = computed(() =>
  visibleSeries.value.map(s => {
    const vals = s.records.map(r => Number(r.value)).filter(v => !isNaN(v));
    const count = vals.length;
    const min = count ? Math.min(...vals) : null;
    const max = count ? Math.max(...vals) : null;
    const avg = count ? vals.reduce((a, b) => a + b, 0) / count : null;
    const latest = count ? vals[vals.length - 1] : null;
    return { key: s.key, color: s.color, label: s.variableName, unit: s.unit, count, min, max, avg, latest };
  })
);

// ==================== Tooltip ====================
const chartMouseLeave = () => { tooltip.value = null; };

// 通过反算像素→时间实现 hover（独立函数，避免计算属性内引用）
const handleChartMouseMove = (ev: MouseEvent) => {
  const g = chartGeometry.value;
  if (g.empty || g.series.length === 0) return;
  const svgEl = ev.currentTarget as SVGSVGElement;
  const rect = svgEl.getBoundingClientRect();
  const px = ((ev.clientX - rect.left) / rect.width) * SVG_W;
  const py = ((ev.clientY - rect.top) / rect.height) * SVG_H;

  // 反算时间域（getX(t) = PAD_X + ((t - tMin)/(tMax - tMin)) * (SVG_W - 2*PAD_X)）
  const inner = px - PAD_X;
  const scale = SVG_W - 2 * PAD_X;
  const tMin = timeDomainForTooltip.value.min;
  const tMax = timeDomainForTooltip.value.max;
  const tVal = tMin + (inner / scale) * (tMax - tMin);

  const items: { color: string; label: string; value: string; bad: boolean }[] = [];
  const allDs = allSeriesPoints.value;
  const tSpan = tMax - tMin || 1;
  g.series.forEach(s => {
    const pts = allDs.find(d => d.key === s.key)?.pts ?? [];
    if (pts.length === 0) return;
    let best = pts[0];
    let bestDist = Infinity;
    for (const p of pts) {
      const dist = Math.abs(p.t - tVal);
      if (dist < bestDist) { bestDist = dist; best = p; }
    }
    // 仅当命中点在时间跨度 10% 范围内才显示（避免序列无数据时仍显示远端首点）
    if (bestDist <= tSpan * 0.1) {
      items.push({ color: s.color, label: s.label, value: best.v.toFixed(2), bad: best.bad });
    }
  });
  if (items.length === 0) { tooltip.value = null; return; }
  tooltip.value = {
    x: px,
    y: py,
    time: new Date(tVal).toISOString(),
    items
  };
};

// 供 tooltip 使用的派生数据（避免在函数内重复构建）
const allSeriesPoints = computed(() =>
  visibleSeries.value.map(s => ({
    key: s.key,
    pts: s.records
      .filter(r => !isNaN(new Date(r.timestamp).getTime()))
      .map(r => ({ t: new Date(r.timestamp).getTime(), v: Number(r.value), bad: r.quality != null && r.quality !== 'Good' }))
      .sort((a, b) => a.t - b.t)
  }))
);
const timeDomainForTooltip = computed(() => {
  let min = Infinity, max = -Infinity;
  allSeriesPoints.value.forEach(d => d.pts.forEach(p => {
    if (p.t < min) min = p.t;
    if (p.t > max) max = p.t;
  }));
  if (!isFinite(min)) return { min: 0, max: 1 };
  if (min === max) { min -= 1; max += 1; }
  return { min, max };
});

const tooltipStyle = computed(() => {
  if (!tooltip.value) return {};
  const leftPct = (tooltip.value.x / SVG_W) * 100;
  const topPct = (tooltip.value.y / SVG_H) * 100;
  const flipX = leftPct > 55;
  return {
    left: `${leftPct}%`,
    top: `${topPct}%`,
    transform: `translate(${flipX ? 'calc(-100% - 12px)' : '12px'}, -50%)`
  };
});

// ==================== 明细表格 ====================
const allTableRecords = computed(() => {
  const rows: Array<{
    id: string;
    deviceName: string;
    deviceKey: string;
    variableName: string;
    variableKey: string;
    value: number;
    unit?: string;
    quality?: string;
    timestamp: string;
  }> = [];
  visibleSeries.value.forEach(s => {
    s.records.forEach(r => {
      rows.push({
        id: r.id,
        deviceName: s.deviceName,
        deviceKey: s.deviceKey,
        variableName: r.variableName || s.variableName,
        variableKey: r.variableKey || s.variableKey,
        value: Number(r.value),
        unit: s.unit,
        quality: r.quality,
        timestamp: r.timestamp
      });
    });
  });
  rows.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());
  return rows;
});
const totalRecordsCount = computed(() => allTableRecords.value.length);
const totalPagesCount = computed(() => Math.ceil(totalRecordsCount.value / PAGE_SIZE) || 1);
const paginatedTableRecords = computed(() => {
  const start = (currentPageNum.value - 1) * PAGE_SIZE;
  return allTableRecords.value.slice(start, start + PAGE_SIZE);
});

// ==================== CSV 导出 ====================
const downloadBlob = (blob: Blob, filename: string) => {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

const handleExportCSV = async () => {
  if (selectedVars.value.length === 0) {
    alert('请先选择至少一个变量再导出');
    return;
  }

  if (isSimulation.value) {
    // 模拟模式：本地导出（后端不可用回退）
    const records = selectedVars.value.flatMap(v =>
      historicalRecords.value.filter(r => r.variableKey === v.variableKey && (r.deviceKey || '') === v.deviceKey)
    );
    if (records.length === 0) {
      alert('当前无可供导出的历史序列记录！');
      return;
    }
    let csv = '\ufeff时间,设备Key,变量Key,变量名,值\n';
    records.forEach(r => {
      csv += `"${r.timestamp}","${r.deviceKey || ''}","${r.variableKey}","${r.variableName}",${r.value}\n`;
    });
    downloadBlob(new Blob([csv], { type: 'text/csv;charset=utf-8;' }), `history_export_${Date.now()}.csv`);
    return;
  }

  try {
    const range = timeframeToRange();
    const blob = await exportHistoryCsv({
      variables: selectedVars.value.map(v => ({ deviceKey: v.deviceKey, variableKey: v.variableKey })),
      start: range.start,
      end: range.end,
      aggregateWindowMs: aggregateWindowMs.value,
      aggregateFn: aggregateFn.value
    });
    const filename = `history_export_${new Date().toISOString().replace(/[:.]/g, '-')}.csv`;
    downloadBlob(blob, filename);
  } catch (err: any) {
    alert(`导出失败：${err.message}`);
  }
};
</script>

<template>
  <div class="h-full flex flex-col text-[#1e293b] dark:text-slate-100 select-none bg-slate-50 dark:bg-transparent overflow-y-auto">

    <!-- 顶部横幅 -->
    <div class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
      <div class="space-y-1">
        <h2 class="font-bold text-base text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
          <Calendar class="w-5 h-5 text-[#1890ff]" />
          历史数据查询
        </h2>
        <p class="text-xs text-slate-500 dark:text-slate-400 font-sans">
          查询历史时序数据，支持多变量对比、时间筛选、聚合降采样与趋势图表展示。
        </p>
      </div>

      <button
        @click="handleExportCSV"
        class="font-bold text-xs bg-emerald-600 hover:bg-emerald-700 text-white px-4 py-2 rounded-lg inline-flex items-center gap-1.5 cursor-pointer self-end md:self-center transition-all shadow-xs active:translate-y-0.5"
      >
        <FileSpreadsheet class="w-4 h-4" />
        导出 CSV
      </button>
    </div>

    <!-- 查询工具栏 -->
    <div class="p-6 bg-slate-50 dark:bg-transparent border-b border-slate-200/60 dark:border-slate-800/60 flex flex-col gap-4 text-left select-none relative z-30">

      <!-- 变量多选 -->
      <div class="w-full relative">
        <label class="block text-[10px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-widest mb-1.5 font-sans">
          选择变量（最多 {{ MAX_SELECTED }} 条对比）
        </label>
        <div class="relative">
          <input
            v-model="searchInput"
            @focus="isInputFocused = true"
            @blur="onSearchBlur"
            type="text"
            placeholder="搜索设备 / 变量..."
            class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg p-2.5 pl-9 text-xs text-slate-800 dark:text-white font-bold focus:outline-none focus:border-[#1890ff] shadow-xs"
          />
          <Search class="absolute left-3 top-3 w-4 h-4 text-slate-400" />
        </div>

        <!-- 下拉选项 -->
        <div
          v-if="isInputFocused"
          class="absolute left-0 right-0 top-[58px] bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg shadow-xl max-h-56 overflow-y-auto z-40 p-1 divide-y divide-slate-50 dark:divide-slate-800"
        >
          <div
            v-for="item in filteredDropdownOptions"
            :key="seriesKey(item.deviceKey, item.variableKey)"
            @mousedown="handleSelectVariable(item)"
            class="p-2.5 hover:bg-sky-50 dark:hover:bg-slate-800 text-[11px] text-slate-700 dark:text-slate-200 font-bold cursor-pointer flex justify-between items-center transition-colors rounded-md"
          >
            <span>{{ item.deviceName }} / {{ item.variableName }} ({{ item.variableKey }})</span>
            <span class="text-[9px] bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400 px-1.5 py-0.5 rounded-md font-mono">{{ item.unit || '-' }}</span>
          </div>

          <div v-if="filteredDropdownOptions.length === 0" class="p-4 text-center text-slate-400 dark:text-slate-500 text-xs">
            暂无匹配变量（请先在数据模型/设备变量中配置并启用采集）
          </div>
        </div>

        <!-- 已选变量 chips -->
        <div v-if="selectedVars.length > 0" class="flex flex-wrap gap-1.5 mt-2">
          <span
            v-for="(v, idx) in selectedVars"
            :key="seriesKey(v.deviceKey, v.variableKey)"
            class="inline-flex items-center gap-1 text-[11px] font-bold px-2.5 py-1 rounded-lg border"
            :class="visibleKeys[seriesKey(v.deviceKey, v.variableKey)] === false
              ? 'bg-slate-100 dark:bg-slate-800 border-slate-200 dark:border-slate-700 text-slate-400 line-through'
              : 'bg-sky-50 dark:bg-sky-950/40 border-sky-200 dark:border-sky-800 text-sky-700 dark:text-sky-300'"
            :style="visibleKeys[seriesKey(v.deviceKey, v.variableKey)] === false ? {} : { borderColor: CHART_COLORS[idx % CHART_COLORS.length] + '66' }"
            @click="toggleSeriesVisible(seriesKey(v.deviceKey, v.variableKey))"
            title="点击显隐曲线"
          >
            <span class="w-2 h-2 rounded-full inline-block" :style="{ background: CHART_COLORS[idx % CHART_COLORS.length] }"></span>
            {{ v.deviceName }}/{{ v.variableName }}
            <button
              class="hover:text-red-500 transition-colors cursor-pointer"
              @click.stop="handleRemoveSelected(seriesKey(v.deviceKey, v.variableKey))"
            >
              <X class="w-3 h-3" />
            </button>
          </span>
        </div>
      </div>

      <!-- 时间范围 + 聚合 -->
      <div class="flex flex-col xl:flex-row gap-4 items-stretch xl:items-end">
        <div class="flex-1">
          <label class="block text-[10px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-widest mb-1.5 font-sans">
            时间范围
          </label>
          <div class="grid grid-cols-6 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg p-0.5 font-bold text-[11px] shadow-xs text-center">
            <button
              v-for="tf in TIMEFRAME_OPTIONS"
              :key="tf.key"
              @click="selectTimeframe(tf.key)"
              class="py-2 rounded-md transition-all cursor-pointer"
              :class="selectedTimeframe === tf.key ? 'bg-slate-900 dark:bg-sky-600 text-white font-bold' : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'"
            >
              {{ tf.label }}
            </button>
          </div>

          <!-- 自定义时间 -->
          <div v-if="selectedTimeframe === 'custom'" class="mt-2 flex flex-wrap items-center gap-2">
            <input
              v-model="customStart"
              @change="onCustomRangeChange"
              type="datetime-local"
              class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs font-bold text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]"
            />
            <span class="text-slate-400 text-xs font-bold">至</span>
            <input
              v-model="customEnd"
              @change="onCustomRangeChange"
              type="datetime-local"
              class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs font-bold text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]"
            />
          </div>
        </div>

        <div class="flex gap-3 items-end">
          <div>
            <label class="block text-[10px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-widest mb-1.5 font-sans">
              聚合粒度
            </label>
            <select
              v-model.number="aggregateWindowMs"
              @change="onAggregationChange"
              class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-2 text-xs font-bold text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff] shadow-xs"
            >
              <option v-for="opt in AGG_OPTIONS" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
            </select>
          </div>
          <div>
            <label class="block text-[10px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-widest mb-1.5 font-sans">
              聚合函数
            </label>
            <select
              v-model="aggregateFn"
              @change="onAggregationChange"
              class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-2 text-xs font-bold text-slate-700 dark:text-slate-200 focus:outline-none focus:border-[#1890ff] shadow-xs"
            >
              <option v-for="opt in AGG_FN_OPTIONS" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
            </select>
          </div>
        </div>
      </div>
    </div>

    <!-- 趋势图 -->
    <div class="px-6 pb-6 shrink-0 text-left">
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs overflow-hidden transition-colors">

        <div class="flex items-center justify-between mb-3 border-b border-slate-100 dark:border-slate-800 pb-3">
          <div class="flex items-center gap-2">
            <TrendingUp class="w-4 h-4 text-emerald-500 animate-pulse" />
            <span class="text-xs font-bold text-slate-800 dark:text-slate-200 uppercase tracking-tight">
              趋势图
              <span v-if="chartGeometry.multi" class="text-amber-500 dark:text-amber-400 text-[10px] font-bold ml-2">
                多变量量纲不同，曲线已分别归一化
              </span>
            </span>
          </div>
          <span class="text-[10px] text-slate-400 dark:text-slate-500 font-mono font-medium">
            {{ isLoading ? '查询中...' : `可见序列 ${chartGeometry.series.length} · 数据点 ${allTableRecords.length} 个` }}
          </span>
        </div>

        <!-- 统计条 -->
        <div v-if="statsBySeries.length > 0" class="flex flex-wrap gap-x-5 gap-y-1 mb-3 pb-2 border-b border-slate-100 dark:border-slate-800/60 text-[10px] font-sans">
          <span v-for="s in statsBySeries" :key="s.key" class="inline-flex items-center gap-1.5 text-slate-500 dark:text-slate-400">
            <span class="w-2 h-2 rounded-full inline-block" :style="{ background: s.color }"></span>
            <b class="text-slate-700 dark:text-slate-200 font-bold">{{ s.label }}</b>
            最大 <b class="text-slate-800 dark:text-slate-100 font-mono">{{ s.max?.toFixed(2) ?? '-' }}</b>
            最小 <b class="text-slate-800 dark:text-slate-100 font-mono">{{ s.min?.toFixed(2) ?? '-' }}</b>
            平均 <b class="text-slate-800 dark:text-slate-100 font-mono">{{ s.avg?.toFixed(2) ?? '-' }}</b>
            最新 <b class="text-slate-800 dark:text-slate-100 font-mono">{{ s.latest?.toFixed(2) ?? '-' }}</b>
            <span class="text-slate-400">({{ s.count }} 点)</span>
          </span>
        </div>

        <div class="w-full relative bg-slate-50/50 dark:bg-slate-950/60 rounded-xl border border-slate-100 dark:border-slate-800 p-2 overflow-x-auto">
          <div class="relative">
            <svg
              v-if="!chartGeometry.empty && chartGeometry.series.length >= 1"
              :viewBox="`0 0 ${SVG_W} ${SVG_H}`"
              class="w-full h-auto min-w-[640px] block"
              @mousemove="handleChartMouseMove"
              @mouseleave="chartMouseLeave"
            >
              <!-- 横向网格 -->
              <line
                v-for="(tick, idx) in chartGeometry.yTicks"
                :key="'gy' + idx"
                :x1="PAD_X"
                :y1="tick.y"
                :x2="SVG_W - PAD_X"
                :y2="tick.y"
                stroke="#cbd5e1"
                stroke-opacity="0.5"
                stroke-width="1"
                stroke-dasharray="3,3"
              />
              <!-- Y 轴刻度 -->
              <text
                v-for="(tick, idx) in chartGeometry.yTicks"
                :key="'ty' + idx"
                :x="PAD_X - 10"
                :y="tick.y + 4"
                fill="#94a3b8"
                font-family="monospace"
                font-size="9"
                font-weight="bold"
                text-anchor="end"
              >
                {{ tick.label }}
              </text>
              <!-- X 轴刻度 -->
              <text
                v-for="(tick, idx) in chartGeometry.xTicks"
                :key="'tx' + idx"
                :x="tick.x"
                :y="SVG_H - 8"
                fill="#94a3b8"
                font-family="monospace"
                font-size="9"
                font-weight="bold"
                text-anchor="middle"
              >
                {{ tick.label }}
              </text>

              <!-- 各曲线 -->
              <g v-for="s in chartGeometry.series" :key="s.key">
                <path
                  :d="s.path"
                  fill="none"
                  :stroke="s.color"
                  stroke-width="2.2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                />
                <g v-for="(c, ci) in s.circles" :key="ci">
                  <circle :cx="c.x" :cy="c.y" r="3.5" :fill="c.bad ? '#ef4444' : '#0f172a'" :stroke="s.color" stroke-width="2" />
                </g>
              </g>

              <!-- 十字线 + 命中高亮 -->
              <g v-if="tooltip">
                <line
                  :x1="tooltip.x"
                  :y1="PAD_Y"
                  :x2="tooltip.x"
                  :y2="SVG_H - PAD_Y"
                  stroke="#64748b"
                  stroke-width="1"
                  stroke-dasharray="4,3"
                />
              </g>
            </svg>

            <!-- 空态 -->
            <div
              v-else
              class="py-16 text-center text-slate-400 dark:text-slate-500 flex flex-col items-center justify-center gap-2"
            >
              <Loader2 v-if="isLoading" class="w-8 h-8 text-slate-300 animate-spin" />
              <AlertCircle v-else class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-bounce" />
              <span class="text-xs">
                <template v-if="selectedVars.length === 0">请先在左侧选择至少一个变量</template>
                <template v-else-if="isLoading">正在从时序库拉取数据...</template>
                <template v-else>在选定的时间范围内，未查询到所选变量的任何时序。</template>
              </span>
            </div>

            <!-- Tooltip 浮层 -->
            <div
              v-if="tooltip && chartGeometry.series.length > 0"
              class="absolute z-20 pointer-events-none bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg shadow-xl px-3 py-2 text-[11px] font-sans min-w-[160px]"
              :style="tooltipStyle"
            >
              <div class="text-[10px] font-mono text-slate-400 dark:text-slate-500 mb-1">{{ tooltip.time }}</div>
              <div v-for="(it, idx) in tooltip.items" :key="idx" class="flex items-center gap-1.5 py-0.5">
                <span class="w-2 h-2 rounded-full inline-block shrink-0" :style="{ background: it.color }"></span>
                <span class="text-slate-600 dark:text-slate-300 font-bold truncate max-w-[140px]">{{ it.label }}</span>
                <span class="font-mono font-bold" :class="it.bad ? 'text-red-500' : 'text-slate-800 dark:text-slate-100'">{{ it.value }}</span>
                <span v-if="it.bad" class="text-red-500 text-[9px]">·劣质</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 明细表格 -->
    <div class="px-6 pb-6 select-none text-left flex-1 min-h-[300px] flex">
      <div class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden flex flex-col justify-between transition-colors">
        <div class="overflow-x-auto flex-1">
          <table class="w-full text-left text-xs font-sans">
            <thead class="bg-slate-50 dark:bg-slate-950/60 border-b border-slate-100 dark:border-slate-800 text-slate-400 dark:text-slate-500 font-bold uppercase tracking-wider text-[10px]">
              <tr>
                <th class="p-3.5 pl-5">采集项 ID</th>
                <th class="p-3.5">设备</th>
                <th class="p-3.5">变量键名 (Variable Key)</th>
                <th class="p-3.5">物标测位中文注释</th>
                <th class="p-3.5 font-mono">核算实测值</th>
                <th class="p-3.5">质量位</th>
                <th class="p-3.5 pr-5 text-right">时间戳 (采样时域)</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100 dark:divide-slate-800 text-slate-700 dark:text-slate-300">
              <tr v-for="rec in paginatedTableRecords" :key="rec.id" class="hover:bg-slate-50/40 dark:hover:bg-slate-800/40 transition-colors">
                <td class="p-3.5 pl-5 font-mono font-medium text-slate-400 dark:text-slate-500">{{ rec.id }}</td>
                <td class="p-3.5 font-medium text-slate-600 dark:text-slate-300">{{ rec.deviceName }}<span class="text-[9px] text-slate-400 font-mono ml-1">{{ rec.deviceKey }}</span></td>
                <td class="p-3.5 font-bold font-mono text-slate-800 dark:text-slate-100">{{ rec.variableKey }}</td>
                <td class="p-3.5 font-medium text-slate-500 dark:text-slate-400">{{ rec.variableName }}</td>
                <td class="p-3.5 font-bold font-mono text-indigo-600 dark:text-indigo-400">
                  {{ rec.value }} <span class="text-[9px] font-sans text-slate-400 dark:text-slate-500">{{ rec.unit }}</span>
                </td>
                <td class="p-3.5">
                  <span
                    v-if="rec.quality && rec.quality !== 'Good'"
                    class="text-[9px] font-bold px-1.5 py-0.5 rounded-md bg-red-50 dark:bg-red-950/50 text-red-500"
                  >
                    {{ rec.quality }}
                  </span>
                  <span v-else class="text-[9px] text-slate-400 font-mono">Good</span>
                </td>
                <td class="p-3.5 pr-5 text-right font-mono text-slate-400 dark:text-slate-500">{{ rec.timestamp }}</td>
              </tr>

              <tr v-if="allTableRecords.length === 0">
                <td colspan="7" class="text-center py-16 text-slate-400 dark:text-slate-500">
                  没有符合检索过滤条件的物标时序块。
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- 分页 -->
        <div v-if="totalPagesCount > 1" class="p-4 border-t border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/60 flex items-center justify-between text-xs font-medium shrink-0">
          <span class="text-slate-400 dark:text-slate-500">
            共计 <b class="text-slate-700 dark:text-slate-200 font-bold">{{ totalRecordsCount }}</b> 采样槽 · 分页 {{ currentPageNum }} / {{ totalPagesCount }}
          </span>

          <div class="flex items-center gap-1">
            <button
              @click="currentPageNum = Math.max(1, currentPageNum - 1)"
              :disabled="currentPageNum === 1"
              class="p-1 px-2.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 hover:bg-slate-50 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 font-bold inline-flex items-center gap-1 cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed select-none active:scale-95 transition-all text-[11px]"
            >
              <ChevronLeft class="w-3.5 h-3.5" /> 上一页
            </button>

            <button
              @click="currentPageNum = Math.min(totalPagesCount, currentPageNum + 1)"
              :disabled="currentPageNum === totalPagesCount"
              class="p-1 px-2.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 hover:bg-slate-50 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 font-bold inline-flex items-center gap-1 cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed select-none active:scale-95 transition-all text-[11px]"
            >
              下一页 <ChevronRight class="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>
    </div>

  </div>
</template>
