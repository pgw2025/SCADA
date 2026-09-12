<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
import { historicalRecords } from '../store/historyStore';
import { systemConfig, addLog } from '../store/index';
import {
  fetchHistoryFromBackend,
  fetchHistoryBatch,
  exportHistoryCsv
} from '../api/historyApi';
import { showToast } from '../services/toastService';
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
  Loader2,
  RefreshCw,
  Maximize2,
  Minimize2,
  LayoutList,
  AlignJustify
} from 'lucide-vue-next';

// ==================== 常量 ====================
const MAX_SELECTED = 8;            // 多变量对比上限（阶段3 P3-15：对齐后端 MaxBatchVariables=8）
const CHART_TARGET_POINTS = 600;   // LTTB 降采样目标点数
const RAW_SINGLE_LIMIT = 10000;    // 原始数据模式单变量取数上限（阶段3 P2-7：对齐后端 50000 中的前端安全值）
const AGG_LIMIT = 2000;            // 聚合模式取数上限（聚合后行数少，2000 足够）
const BASE_SVG_W = 800;
const BASE_SVG_H = 240;
const BASE_PAD_X = 50;
const BASE_PAD_Y = 30;
// 全屏旋转层 viewBox 基准宽度（横向虚拟像素），高度按旋转层实际宽高比动态换算
const FULLSCREEN_SVG_W = 1000;
const CHART_COLORS = ['#38bdf8', '#34d399', '#f59e0b', '#f472b6', '#a78bfa', '#f87171', '#22d3ee', '#a3e635'];
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
const selectedTimeframe = ref<TimeframeKey>('hour');
const customStart = ref('');
const customEnd = ref('');
const aggregateWindowMs = ref<number>(0);
const aggregateFn = ref<string>('mean');
const currentPageNum = ref(1);
const isLoading = ref(false);
const seriesList = ref<HistorySeries[]>([]);
const visibleKeys = ref<Record<string, boolean>>({});
const queryError = ref('');   // 阶段5 P3-10：查询失败错误态（空串=无错误）
// Y 轴显示模式：percent=百分比归一化（多变量默认）；value=共享数值轴（仅同单位时可用）
const yAxisMode = ref<'percent' | 'value'>('value');
// X 轴时间方向：true=最新在左，false=最新在右
const xNewestFirst = ref(true);
// 缩放子窗口（null=全量）。用于放大查看秒级变化先后
const zoomRange = ref<{ min: number; max: number } | null>(null);
// 全屏图表模式（手机端体验优化）
const isFullscreen = ref(false);
// 移动端视图切换：chart=趋势图，table=数据明细（桌面端与移动端统一，二选一显示）
const mobileView = ref<'chart' | 'table'>('chart');
// 是否桌面端（≥768px）。桌面端全屏不隐藏无关元素、不旋转图表
const isDesktop = ref(window.matchMedia('(min-width: 768px)').matches);
// 移动端数据明细视图模式：card=卡片流，compact=紧凑列表（与系统日志页一致，localStorage 独立记忆）
const mobileViewMode = ref<'card' | 'compact'>(
  (localStorage.getItem('scada_history_mobile_view') as 'card' | 'compact') || 'card'
);
const setMobileViewMode = (mode: 'card' | 'compact') => {
  mobileViewMode.value = mode;
  localStorage.setItem('scada_history_mobile_view', mode);
};
// 选中的明细记录（用于移动端底部抽屉详情）
const selectedRecordDetail = ref<null | {
  id: string; deviceName: string; deviceKey: string; variableName: string;
  variableKey: string; value: number; unit?: string; quality?: string; timestamp: string;
}>(null);
const openRecordDetail = (rec: {
  id: string; deviceName: string; deviceKey: string; variableName: string;
  variableKey: string; value: number; unit?: string; quality?: string; timestamp: string;
}) => { selectedRecordDetail.value = rec; };
const closeRecordDetail = () => { selectedRecordDetail.value = null; };
// 移动端全屏（isFullscreen && !isDesktop）→ 图表旋转 90° 横屏看图
const isMobileFullscreen = computed(() => isFullscreen.value && !isDesktop.value);
// 实际视口尺寸（px），用于移动端全屏旋转铺满（避开 100vh 受地址栏影响的问题）
const viewportW = ref(window.innerWidth);
const viewportH = ref(window.innerHeight);
const tooltip = ref<{ x: number; y: number; time: string; items: { color: string; label: string; value: string; bad: boolean }[] } | null>(null);

// ==================== 图表坐标系（响应式，全屏按旋转层比例缩放避免变形） ====================
// 图表缩放系数：非全屏=1；手机全屏= FULLSCREEN_SVG_W / BASE_SVG_W（等比例放大坐标与字号）
const chartScale = computed(() => (isMobileFullscreen.value ? FULLSCREEN_SVG_W / BASE_SVG_W : 1));
// viewBox 尺寸：手机全屏时高度按旋转层实际宽高比换算，使 viewBox 比例与画布一致 → meet 等比缩放即铺满且零变形
const SVG_W = computed(() => Math.round(BASE_SVG_W * chartScale.value));
const SVG_H = computed(() => {
  if (!isMobileFullscreen.value) return BASE_SVG_H;
  const w = viewportW.value, h = viewportH.value;
  const ratio = (w > 0 && h > 0) ? w / h : BASE_SVG_H / BASE_SVG_W;
  return Math.round(FULLSCREEN_SVG_W * ratio);
});
const PAD_X = computed(() => Math.round(BASE_PAD_X * chartScale.value));
const PAD_Y = computed(() => Math.round(BASE_PAD_Y * chartScale.value));

// 后端事件时间为 UTC，统一转成本地时间显示
const fmtTime = (ts?: string | null) => {
  if (!ts) return '--';
  const d = new Date(ts);
  if (isNaN(d.getTime())) return ts;
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
};

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
    // 原始数据模式 vs 聚合模式：取数上限不同（聚合后行数少，原始模式取更多但总量受控）。
    const isAggregated = aggregateWindowMs.value > 0;
    const singleLimit = isAggregated ? AGG_LIMIT : RAW_SINGLE_LIMIT;
    let merged: SeriesInput[];
    if (selectedVars.value.length === 1) {
      const v = selectedVars.value[0];
      const records = await fetchHistoryFromBackend({
        deviceKey: v.deviceKey,
        variableKey: v.variableKey,
        limit: singleLimit,
        start: range.start,
        end: range.end,
        aggregateWindowMs: aggregateWindowMs.value,
        aggregateFn: aggregateFn.value
      });
      merged = [{ deviceKey: v.deviceKey, variableKey: v.variableKey, variableName: v.variableName, deviceName: v.deviceName, unit: v.unit, records }];
    } else {
      // 批量原始模式：每变量 limit 按总量 10000 均摊，避免总量爆内存。
      const batchLimit = isAggregated ? AGG_LIMIT : Math.floor(RAW_SINGLE_LIMIT / selectedVars.value.length);
      const items = await fetchHistoryBatch({
        variables: selectedVars.value.map(v => ({ deviceKey: v.deviceKey, variableKey: v.variableKey })),
        limit: batchLimit,
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
    queryError.value = '';
  } catch (err: any) {
    // 阶段5 P3-10：区分「查询失败」与「无数据」，失败给出明确错误态 + 重试入口。
    queryError.value = err?.message || '历史查询失败，请检查后端服务与网络连接';
    seriesList.value = [];
    visibleKeys.value = {};
    showToast(queryError.value, 'error');
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
  zoomRange.value = null; // 数据变化时复位缩放，避免残留旧窗口
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
  } catch (err: any) {
    addLog('历史查询', `加载设备变量列表失败: ${err.message}（已回退演示变量）`, 'warning');
    dynamicVariables.value = [];
  }
};

// 监听视口尺寸变化（含地址栏伸缩、旋转），同步 viewport 尺寸
const onResize = () => { viewportW.value = window.innerWidth; viewportH.value = window.innerHeight; };

onMounted(() => {
  // 全局 mouseup：拖出 SVG 后松开鼠标也要结束拖动
  window.addEventListener('mouseup', handleChartMouseUp);
  // 监听断点变化，同步 isDesktop（桌面端全屏不隐藏、不旋转）
  const mql = window.matchMedia('(min-width: 768px)');
  const onMqlChange = (e: MediaQueryListEvent) => { isDesktop.value = e.matches; };
  mql.addEventListener('change', onMqlChange);
  window.addEventListener('resize', onResize);
  if (isSimulation.value) {
    // 模拟模式：仅加载演示变量，不默认选中、不自动查询
    return;
  } else {
    loadVariableOptions();
  }
});

onBeforeUnmount(() => {
  window.removeEventListener('mouseup', handleChartMouseUp);
  window.removeEventListener('resize', onResize);
});

// ==================== 趋势图几何 ====================
const visibleSeries = computed(() => seriesList.value.filter(s => visibleKeys.value[s.key] !== false));

// 全量数据时间域（所有可见曲线的时间 min/max）
const fullTimeDomain = computed(() => {
  let min = Infinity, max = -Infinity;
  visibleSeries.value.forEach(s => s.records.forEach(r => {
    const t = new Date(r.timestamp).getTime();
    if (isNaN(t)) return;
    if (t < min) min = t;
    if (t > max) max = t;
  }));
  if (!isFinite(min)) return { min: 0, max: 1 };
  if (min === max) { min -= 1; max += 1; }
  return { min, max };
});

// 有效时间域：缩放子窗口（若有）优先，否则全量域
const effectiveTimeDomain = computed(() => {
  if (zoomRange.value) return zoomRange.value;
  return fullTimeDomain.value;
});

// 所有可见曲线是否可共享数值轴（用于「具体值」判定）
// 判定：数量 ≥2，且单位「要么全部相同、要么全部为空（均无量纲）」。仅当单位各不相同（有的有、有的无、或值不同）才视为不可共享。
const isSameUnit = computed(() => {
  const vs = visibleSeries.value;
  if (vs.length < 2) return false;
  const units = vs.map(s => (s.unit || '').trim());
  const first = units[0];
  // 全部为空 → 均无量纲，可共享数值轴
  if (units.every(u => u === '')) return true;
  // 否则要求所有单位完全一致（且不能有空串混入）
  if (!first) return false;
  return units.every(u => u === first);
});

// 实际生效的 Y 轴模式：value 仅在「同单位 + 用户选了 value」时生效，否则回退 percent
const effectiveValueMode = computed(() => yAxisMode.value === 'value' && isSameUnit.value);

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
  // 用「有效时间域」跨度（缩放后自适应），而非时间窗选项跨度
  const span = effectiveTimeDomain.value.max - effectiveTimeDomain.value.min;
  const p = (n: number) => String(n).padStart(2, '0');
  if (span > 0 && span <= 60 * 1000) {
    // ≤1 分钟：显示到秒（如 14:32:07），便于分辨秒级先后
    return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
  }
  if (span > 60 * 1000 && span <= 24 * 3600 * 1000) {
    return `${p(d.getHours())}:${p(d.getMinutes())}`;
  }
  if (span > 24 * 3600 * 1000 && span <= 31 * 24 * 3600 * 1000) {
    return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
  }
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
};

const chartGeometry = computed(() => {
  // 解构为纯数字（SVG_W/SVG_H/PAD_X/PAD_Y 是 computed ref，函数体内需 .value）
  const W = SVG_W.value, H = SVG_H.value, PX = PAD_X.value, PY = PAD_Y.value;
  // 各可见序列原始点（按时间升序）
  const ds = visibleSeries.value.map(s => {
    const pts: ChartPoint[] = s.records
      .filter(r => !isNaN(new Date(r.timestamp).getTime()))
      .map(r => ({ t: new Date(r.timestamp).getTime(), v: Number(r.value), bad: r.quality != null && r.quality !== 'Good' }))
      .sort((a, b) => a.t - b.t);
    return { key: s.key, color: s.color, unit: s.unit, label: s.variableName, pts };
  });

  // 全量时间域（用于空态/单点判断）
  let tMin = Infinity, tMax = -Infinity;
  ds.forEach(d => d.pts.forEach(p => {
    if (p.t < tMin) tMin = p.t;
    if (p.t > tMax) tMax = p.t;
  }));
  if (!isFinite(tMin) || ds.length === 0) {
    return { series: [] as any[], yTicks: [] as any[], xTicks: [] as any[], multi: false, empty: true, useSharedAxis: false, sharedMin: 0, sharedMax: 1 };
  }

  // 有效时间域（缩放子窗口优先），映射据此绘制
  const eMin = effectiveTimeDomain.value.min;
  const eMax = effectiveTimeDomain.value.max;
  const tSpan = eMax - eMin;
  // X 轴时间方向：xNewestFirst 时最新在最左（eMax 落于 PX）；否则最新在最右（eMin 落于 PX）
  const getX = (t: number) => xNewestFirst.value
    ? PX + ((eMax - t) / tSpan) * (W - 2 * PX)
    : PX + ((t - eMin) / tSpan) * (W - 2 * PX);
  const getYForValue = (v: number, min: number, max: number) => {
    const span = max - min || 1;
    return H - PY - ((v - min) / span) * (H - 2 * PY);
  };

  const multi = ds.length > 1;

  // 共享数值轴模式：全局值域（所有可见曲线的 min/max 并集）
  const useSharedAxis = multi && effectiveValueMode.value;
  let sharedMin = Infinity, sharedMax = -Infinity;
  if (useSharedAxis) {
    ds.forEach(d => d.pts.forEach(p => {
      if (p.v < sharedMin) sharedMin = p.v;
      if (p.v > sharedMax) sharedMax = p.v;
    }));
    if (!isFinite(sharedMin)) { sharedMin = 0; sharedMax = 1; }
    if (sharedMin === sharedMax) { sharedMin -= 1; sharedMax += 1; }
  }

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
    if (useSharedAxis) {
      // 共享数值轴：统一用全局值域，各曲线可真实比较高低
      min = sharedMin;
      max = sharedMax;
    } else if (!multi) {
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
    // 数值标签防重叠：按 X 方向扫描，与上一个已标注点水平间距不足 MIN_LABEL_GAP 时跳过该点标签
    const MIN_LABEL_GAP = 42; // 最小水平间距（SVG 单位）
    let lastLabelX = -Infinity;
    const labeledCircles = circles.map(c => {
      const show = c.x - lastLabelX >= MIN_LABEL_GAP;
      if (show) lastLabelX = c.x;
      return { ...c, showLabel: show };
    });
    // 最新数据点（时间最大）：用于端点高亮标记「哪边是最新」
    const latest = d.pts.length ? d.pts[d.pts.length - 1] : null;
    const latestPoint = latest
      ? { x: getX(latest.t), y: norm(latest.v), v: latest.v, bad: latest.bad }
      : null;
    return { key: d.key, color: d.color, label: d.label, unit: d.unit, path, circles: labeledCircles, min, max, norm, latestPoint };
  });

  // Y 轴刻度：共享数值轴模式/单曲线显示数值；多曲线独立归一化显示百分比
  let yTicks: { y: number; label: string }[] = [];
  if (useSharedAxis) {
    // 共享数值轴：显示真实数值刻度
    yTicks = [0, 1, 2, 3, 4].map(i => {
      const val = sharedMin + ((sharedMax - sharedMin) * i) / 4;
      return { y: getYForValue(val, sharedMin, sharedMax), label: val.toFixed(1) };
    });
  } else if (multi) {
    yTicks = [0, 0.25, 0.5, 0.75, 1].map(f => ({
      y: H - PY - f * (H - 2 * PY),
      label: `${Math.round(f * 100)}%`
    }));
  } else if (series.length === 1) {
    const s = series[0];
    yTicks = [0, 1, 2, 3, 4].map(i => {
      const val = s.min + ((s.max - s.min) * i) / 4;
      return { y: s.norm(val), label: val.toFixed(1) };
    });
  }

  // X 轴刻度：5 等分时间标签（方向随 xNewestFirst，基于有效时间域）
  const xTicks = [0, 1, 2, 3, 4].map(i => {
    const t = xNewestFirst.value ? eMax - (tSpan * i) / 4 : eMin + (tSpan * i) / 4;
    return { x: getX(t), label: formatTimeLabel(t) };
  });

  return { series, yTicks, xTicks, multi, empty: false, getX, useSharedAxis, sharedMin, sharedMax, eMin, eMax };
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
const chartMouseLeave = () => {
  tooltip.value = null;
  // 拖出 SVG 时结束拖动（框选则取消，平移则保留当前位置）
  if (dragState.value?.type === 'brush') dragState.value = null;
};

// ==================== 缩放 ====================
// 判断是否处于缩放态（用于 UI 提示）
const isZoomed = computed(() => zoomRange.value !== null);

// 滚轮缩放：以光标位置为锚点缩放有效时间域；缩到覆盖全量时复位
const handleChartWheel = (ev: WheelEvent) => {
  const g = chartGeometry.value;
  if (g.empty || g.series.length === 0) return;
  ev.preventDefault();
  const svgEl = ev.currentTarget as SVGSVGElement;
  const rect = svgEl.getBoundingClientRect();
  const px = ((ev.clientX - rect.left) / rect.width) * SVG_W.value;
  const scale = SVG_W.value - 2 * PAD_X.value;
  const ratioPx = Math.min(1, Math.max(0, (px - PAD_X.value) / scale));
  const curMin = g.eMin, curMax = g.eMax;
  const curSpan = curMax - curMin;

  // 缩放系数：滚轮向下（deltaY>0）缩小窗口，向上放大
  const factor = ev.deltaY > 0 ? 1.25 : 0.8;
  let newSpan = curSpan * factor;
  // 最小窗口：1 秒（避免无限放大到单点）
  if (newSpan < 1000) newSpan = 1000;

  // 光标处绝对时间 t0（方向无关地由 ratioPx 反推）
  const t0 = xNewestFirst.value
    ? curMax - ratioPx * curSpan
    : curMin + ratioPx * curSpan;

  // 缩放后保持 t0 仍位于 ratioPx 比例处
  let newMin: number, newMax: number;
  if (xNewestFirst.value) {
    newMax = t0 + ratioPx * newSpan;
    newMin = newMax - newSpan;
  } else {
    newMin = t0 - ratioPx * newSpan;
    newMax = newMin + newSpan;
  }

  // 夹紧到全量域
  const fullMin = fullTimeDomain.value.min;
  const fullMax = fullTimeDomain.value.max;
  const clampedMin = Math.max(newMin, fullMin);
  const clampedMax = Math.min(newMax, fullMax);
  // 若已覆盖全量（缩小回全量），复位
  if (clampedMin <= fullMin && clampedMax >= fullMax) {
    zoomRange.value = null;
    return;
  }
  zoomRange.value = { min: clampedMin, max: clampedMax };
};

// 双击复位缩放
const handleChartDblClick = () => {
  zoomRange.value = null;
};

// ==================== 平移 / 框选 ====================
// 拖动状态：null=无，{type:'pan'} 平移，{type:'brush'} 框选放大
const dragState = ref<{ type: 'pan' | 'brush'; startPx: number; startMin: number; startMax: number } | null>(null);
// 框选当前末端像素（用于实时绘制矩形）
const brushEndPx = ref(0);
// 是否正在拖动（平移或框选）
const isDragging = computed(() => dragState.value !== null);

// 将客户端坐标换算为 SVG 绘图坐标 px
const clientToSvgPx = (ev: MouseEvent, el: SVGSVGElement) => {
  const rect = el.getBoundingClientRect();
  return ((ev.clientX - rect.left) / rect.width) * SVG_W.value;
};

// 鼠标按下：Shift=框选放大；否则=平移（仅已缩放态可平移）
const handleChartMouseDown = (ev: MouseEvent) => {
  const g = chartGeometry.value;
  if (g.empty || g.series.length === 0) return;
  const svgEl = ev.currentTarget as SVGSVGElement;
  const px = clientToSvgPx(ev, svgEl);
  if (ev.shiftKey) {
    // 框选放大：无需已缩放
    dragState.value = { type: 'brush', startPx: px, startMin: g.eMin, startMax: g.eMax };
    brushEndPx.value = px;
  } else if (isZoomed.value) {
    // 平移：仅在已缩放态有意义
    dragState.value = { type: 'pan', startPx: px, startMin: g.eMin, startMax: g.eMax };
    ev.preventDefault();
  }
};

// 鼠标移动：拖动中处理平移/框选，否则显示 tooltip
const handleChartMouseMove = (ev: MouseEvent) => {
  const g = chartGeometry.value;
  if (g.empty || g.series.length === 0) return;
  const svgEl = ev.currentTarget as SVGSVGElement;

  // 拖动中：平移 / 框选
  if (dragState.value) {
    const px = clientToSvgPx(ev, svgEl);
    const ds = dragState.value;
    if (ds.type === 'brush') {
      brushEndPx.value = px;
      return;
    }
    // 平移：把像素位移换算成时间位移 Δt
    const rect = svgEl.getBoundingClientRect();
    const scale = SVG_W.value - 2 * PAD_X.value;
    const dxPx = px - ds.startPx;
    const startSpan = ds.startMax - ds.startMin;
    const dt = (dxPx / scale) * startSpan;
    // 平移方向（抓取语义：内容跟随鼠标移动）
    // 最新在左时 getX=(eMax-t)/span，内容向右=窗口更新(eMax增大)=时间增大，与 dxPx 同号；
    // 最新在右时 getX=(t-eMin)/span，内容向右=窗口更旧=时间减小，与 dxPx 反号。
    const signedDt = xNewestFirst.value ? dt : -dt;
    let newMin = ds.startMin + signedDt;
    let newMax = ds.startMax + signedDt;
    // 夹紧到全量域
    const fullMin = fullTimeDomain.value.min;
    const fullMax = fullTimeDomain.value.max;
    const span = newMax - newMin;
    if (newMin < fullMin) { newMin = fullMin; newMax = fullMin + span; }
    if (newMax > fullMax) { newMax = fullMax; newMin = fullMax - span; }
    zoomRange.value = { min: newMin, max: newMax };
    tooltip.value = null;
    return;
  }

  // 非拖动：tooltip
  const rect = svgEl.getBoundingClientRect();
  const px = ((ev.clientX - rect.left) / rect.width) * SVG_W.value;
  const py = ((ev.clientY - rect.top) / rect.height) * SVG_H.value;
  showTooltipAt(px, py);
};

// 按 SVG 坐标显示 tooltip（供鼠标移动与触屏点按共用）
const showTooltipAt = (px: number, py: number) => {
  const g = chartGeometry.value;
  if (g.empty || g.series.length === 0) return;

  // 反算时间域（方向随 xNewestFirst，基于有效时间域）
  const inner = px - PAD_X.value;
  const scale = SVG_W.value - 2 * PAD_X.value;
  const tMin = g.eMin;
  const tMax = g.eMax;
  const tVal = xNewestFirst.value
    ? tMax - (inner / scale) * (tMax - tMin)
    : tMin + (inner / scale) * (tMax - tMin);

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
    time: fmtTime(new Date(tVal).toISOString()),
    items
  };
};

// 鼠标松开：结束平移 / 完成框选放大
const handleChartMouseUp = () => {
  const ds = dragState.value;
  if (!ds) return;
  if (ds.type === 'brush') {
    // 框选放大：将 [startPx, brushEndPx] 映射到时间区间
    const g = chartGeometry.value;
    const scale = SVG_W.value - 2 * PAD_X.value;
    const p1 = Math.min(ds.startPx, brushEndPx.value);
    const p2 = Math.max(ds.startPx, brushEndPx.value);
    // 像素 → 时间（基于框选时的有效域 g.eMin/g.eMax，方向相关）
    const span = g.eMax - g.eMin;
    const tOfPx = (p: number) => xNewestFirst.value
      ? g.eMax - ((p - PAD_X.value) / scale) * span
      : g.eMin + ((p - PAD_X.value) / scale) * span;
    let t1 = tOfPx(p1);
    let t2 = tOfPx(p2);
    if (t1 > t2) { const tmp = t1; t1 = t2; t2 = tmp; }
    // 最小框选窗口 1 秒；太窄视为误触忽略
    if (t2 - t1 >= 1000) {
      zoomRange.value = { min: t1, max: t2 };
    }
  }
  dragState.value = null;
};

// ==================== 触屏手势 ====================
// 触屏状态：单指（平移/点按）或双指（捏合缩放）
const touchState = ref<{
  mode: 'tap' | 'pan' | 'pinch';
  startX: number;         // 单指起始 px（用于判定点按 vs 平移）
  startY: number;
  startMin: number;       // 手势起点有效域
  startMax: number;
  startSpan: number;
  pinchStartDist: number; // 双指起始距离
  pinchStartSpan: number;
  moved: boolean;         // 是否已发生显著移动
} | null>(null);

const handleChartTouchStart = (ev: TouchEvent) => {
  const g = chartGeometry.value;
  if (g.empty || g.series.length === 0) return;
  const svgEl = ev.currentTarget as SVGSVGElement;
  const rect = svgEl.getBoundingClientRect();
  const toPx = (t: Touch) => ({
    x: ((t.clientX - rect.left) / rect.width) * SVG_W.value,
    y: ((t.clientY - rect.top) / rect.height) * SVG_H.value
  });

  if (ev.touches.length === 2) {
    // 双指：捏合缩放
    const t1 = toPx(ev.touches[0]);
    const t2 = toPx(ev.touches[1]);
    const dist = Math.hypot(t2.x - t1.x, t2.y - t1.y);
    touchState.value = {
      mode: 'pinch',
      startX: (t1.x + t2.x) / 2,
      startY: (t1.y + t2.y) / 2,
      startMin: g.eMin,
      startMax: g.eMax,
      startSpan: g.eMax - g.eMin,
      pinchStartDist: dist,
      pinchStartSpan: g.eMax - g.eMin,
      moved: true
    };
    ev.preventDefault();
    return;
  }

  if (ev.touches.length === 1) {
    const p = toPx(ev.touches[0]);
    touchState.value = {
      mode: 'tap',
      startX: p.x,
      startY: p.y,
      startMin: g.eMin,
      startMax: g.eMax,
      startSpan: g.eMax - g.eMin,
      pinchStartDist: 0,
      pinchStartSpan: 0,
      moved: false
    };
  }
};

const handleChartTouchMove = (ev: TouchEvent) => {
  const ts = touchState.value;
  if (!ts) return;
  const g = chartGeometry.value;
  const svgEl = ev.currentTarget as SVGSVGElement;
  const rect = svgEl.getBoundingClientRect();
  const toPx = (t: Touch) => ({
    x: ((t.clientX - rect.left) / rect.width) * SVG_W.value,
    y: ((t.clientY - rect.top) / rect.height) * SVG_H.value
  });

  if (ts.mode === 'pinch' && ev.touches.length === 2) {
    // 捏合缩放
    const t1 = toPx(ev.touches[0]);
    const t2 = toPx(ev.touches[1]);
    const dist = Math.hypot(t2.x - t1.x, t2.y - t1.y);
    if (ts.pinchStartDist === 0) return;
    const factor = ts.pinchStartDist / dist; // 张开(dist增大)→factor<1 放大
    let newSpan = ts.pinchStartSpan * factor;
    if (newSpan < 1000) newSpan = 1000;
    const fullMin = fullTimeDomain.value.min;
    const fullMax = fullTimeDomain.value.max;
    if (newSpan >= fullMax - fullMin) {
      zoomRange.value = null;
      return;
    }
    // 以两指中心为锚点
    const ratioPx = Math.min(1, Math.max(0, (ts.startX - PAD_X.value) / (SVG_W.value - 2 * PAD_X.value)));
    const t0 = xNewestFirst.value
      ? ts.startMax - ratioPx * ts.startSpan
      : ts.startMin + ratioPx * ts.startSpan;
    let newMin: number, newMax: number;
    if (xNewestFirst.value) {
      newMax = t0 + ratioPx * newSpan;
      newMin = newMax - newSpan;
    } else {
      newMin = t0 - ratioPx * newSpan;
      newMax = newMin + newSpan;
    }
    const clampedMin = Math.max(newMin, fullMin);
    const clampedMax = Math.min(newMax, fullMax);
    if (clampedMin <= fullMin && clampedMax >= fullMax) { zoomRange.value = null; return; }
    zoomRange.value = { min: clampedMin, max: clampedMax };
    ev.preventDefault();
    return;
  }

  if (ts.mode === 'tap' && ev.touches.length === 1) {
    const p = toPx(ev.touches[0]);
    const dx = p.x - ts.startX;
    const dy = p.y - ts.startY;
    // 位移超过阈值 → 切换为平移（仅缩放态）；同时拦截页面滚动
    if (Math.hypot(dx, dy) > 8) {
      ts.mode = 'pan';
      ts.moved = true;
    }
    if (ts.mode === 'pan') {
      if (isZoomed.value) {
        // 平移：像素位移 → 时间位移
        const scale = SVG_W.value - 2 * PAD_X.value;
        const dt = (dx / scale) * ts.startSpan;
        const signedDt = xNewestFirst.value ? dt : -dt;
        let newMin = ts.startMin + signedDt;
        let newMax = ts.startMax + signedDt;
        const fullMin = fullTimeDomain.value.min;
        const fullMax = fullTimeDomain.value.max;
        const span = newMax - newMin;
        if (newMin < fullMin) { newMin = fullMin; newMax = fullMin + span; }
        if (newMax > fullMax) { newMax = fullMax; newMin = fullMax - span; }
        zoomRange.value = { min: newMin, max: newMax };
        tooltip.value = null;
      }
      ev.preventDefault();
      return;
    }
  }
};

const handleChartTouchEnd = (ev: TouchEvent) => {
  const ts = touchState.value;
  if (!ts) return;
  // 点按（无显著移动）：显示 tooltip
  if (ts.mode === 'tap' && !ts.moved) {
    // 用最后一次触摸坐标（可能在 touches 里）
    if (ev.changedTouches && ev.changedTouches.length > 0) {
      const svgEl = ev.currentTarget as SVGSVGElement;
      const rect = svgEl.getBoundingClientRect();
      const t = ev.changedTouches[0];
      const px = ((t.clientX - rect.left) / rect.width) * SVG_W.value;
      const py = ((t.clientY - rect.top) / rect.height) * SVG_H.value;
      showTooltipAt(px, py);
    }
  }
  // 双指结束：若剩单指，转入平移起点
  if (ts.mode === 'pinch' && ev.touches.length === 1) {
    const svgEl = ev.currentTarget as SVGSVGElement;
    const rect = svgEl.getBoundingClientRect();
    const t = ev.touches[0];
    const px = ((t.clientX - rect.left) / rect.width) * SVG_W.value;
    const py = ((t.clientY - rect.top) / rect.height) * SVG_H.value;
    const g = chartGeometry.value;
    touchState.value = {
      mode: 'tap',
      startX: px,
      startY: py,
      startMin: g.eMin,
      startMax: g.eMax,
      startSpan: g.eMax - g.eMin,
      pinchStartDist: 0,
      pinchStartSpan: 0,
      moved: false
    };
    return;
  }
  if (ev.touches.length === 0) {
    touchState.value = null;
  }
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

const tooltipStyle = computed(() => {
  if (!tooltip.value) return {};
  const leftPct = (tooltip.value.x / SVG_W.value) * 100;
  const topPct = (tooltip.value.y / SVG_H.value) * 100;
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
    <div v-show="isDesktop || !isFullscreen" class="bg-white dark:bg-slate-900 p-5 border-b border-slate-200 dark:border-slate-800 shadow-sm shrink-0 flex flex-col md:flex-row md:items-center justify-between gap-4 text-left transition-colors">
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
    <div v-show="isDesktop || !isFullscreen" class="p-3 sm:p-6 bg-slate-50 dark:bg-transparent border-b border-slate-200/60 dark:border-slate-800/60 flex flex-col gap-4 text-left select-none relative z-30">

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
          <div class="flex overflow-x-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg p-0.5 font-bold text-[11px] shadow-xs">
            <button
              v-for="tf in TIMEFRAME_OPTIONS"
              :key="tf.key"
              @click="selectTimeframe(tf.key)"
              class="py-2 px-3 rounded-md transition-all cursor-pointer shrink-0 whitespace-nowrap"
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

    <!-- 「趋势图 / 数据明细」视图切换（固定置顶，切换不跳位） -->
    <div v-show="isDesktop || !isFullscreen" class="px-3 sm:px-6 pb-3">
      <div class="flex bg-slate-100 dark:bg-slate-800 rounded-lg p-1 gap-1 font-bold text-[12px] md:max-w-md">
        <button
          @click="mobileView = 'chart'"
          class="flex-1 py-2 rounded-md transition-all cursor-pointer inline-flex items-center justify-center gap-1.5"
          :class="mobileView === 'chart' ? 'bg-white dark:bg-slate-900 text-indigo-600 dark:text-indigo-400 shadow-sm' : 'text-slate-500 dark:text-slate-400'"
        >
          <TrendingUp class="w-3.5 h-3.5" /> 趋势图
        </button>
        <button
          @click="mobileView = 'table'"
          class="flex-1 py-2 rounded-md transition-all cursor-pointer inline-flex items-center justify-center gap-1.5"
          :class="mobileView === 'table' ? 'bg-white dark:bg-slate-900 text-indigo-600 dark:text-indigo-400 shadow-sm' : 'text-slate-500 dark:text-slate-400'"
        >
          <FileSpreadsheet class="w-3.5 h-3.5" /> 数据明细
        </button>
      </div>
    </div>

    <!-- 趋势图 -->
    <div v-show="mobileView === 'chart'" class="px-3 sm:px-6 pb-6 shrink-0 text-left">
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-xs overflow-hidden transition-colors">

        <div v-show="isDesktop || !isFullscreen" class="flex flex-wrap items-center justify-between gap-2 mb-3 border-b border-slate-100 dark:border-slate-800 pb-3">
          <div class="flex items-center gap-2">
            <TrendingUp class="w-4 h-4 text-emerald-500 animate-pulse" />
            <span class="text-xs font-bold text-slate-800 dark:text-slate-200 uppercase tracking-tight">
              趋势图
              <span v-if="chartGeometry.useSharedAxis" class="text-emerald-600 dark:text-emerald-400 text-[10px] font-bold ml-2">
                共享数值轴{{ chartGeometry.series[0]?.unit ? ` · ${chartGeometry.series[0].unit}` : '' }}
              </span>
              <span v-else-if="chartGeometry.multi" class="text-amber-500 dark:text-amber-400 text-[10px] font-bold ml-2">
                多变量量纲不同，曲线已分别归一化
              </span>
            </span>
          </div>

          <div class="flex items-center gap-2 overflow-x-auto max-w-full">
            <!-- 全屏图表 -->
            <button
              v-if="chartGeometry.series.length >= 1"
              @click="isFullscreen = !isFullscreen"
              class="shrink-0 p-1.5 rounded-lg bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 transition-colors cursor-pointer"
              :title="isFullscreen ? '退出全屏' : '全屏查看图表'"
            >
              <Minimize2 v-if="isFullscreen" class="w-3.5 h-3.5" />
              <Maximize2 v-else class="w-3.5 h-3.5" />
            </button>

            <!-- X 轴时间方向切换 -->
            <div
              v-if="chartGeometry.series.length >= 1"
              class="flex items-center gap-1 bg-slate-100 dark:bg-slate-800 rounded-lg p-0.5 shrink-0"
              title="切换 X 轴时间方向：最新在左 / 最新在右"
            >
              <button
                @click="xNewestFirst = true"
                class="px-2.5 py-1 rounded-md text-[10px] font-bold transition-all cursor-pointer"
                :class="xNewestFirst ? 'bg-white dark:bg-slate-600 text-slate-800 dark:text-white shadow-sm' : 'text-slate-500 dark:text-slate-400 hover:text-slate-700'"
              >
                最新在左
              </button>
              <button
                @click="xNewestFirst = false"
                class="px-2.5 py-1 rounded-md text-[10px] font-bold transition-all cursor-pointer"
                :class="!xNewestFirst ? 'bg-white dark:bg-slate-600 text-slate-800 dark:text-white shadow-sm' : 'text-slate-500 dark:text-slate-400 hover:text-slate-700'"
              >
                最新在右
              </button>
            </div>

            <!-- Y 轴显示模式切换 -->
            <div
              v-if="chartGeometry.multi"
              class="flex items-center gap-1 bg-slate-100 dark:bg-slate-800 rounded-lg p-0.5"
              title="切换 Y 轴显示：百分比 / 具体数值（仅所选变量单位一致时可用具体值）"
            >
              <button
                @click="yAxisMode = 'percent'"
                class="px-2.5 py-1 rounded-md text-[10px] font-bold transition-all cursor-pointer"
                :class="yAxisMode === 'percent' ? 'bg-white dark:bg-slate-600 text-slate-800 dark:text-white shadow-sm' : 'text-slate-500 dark:text-slate-400 hover:text-slate-700'"
              >
                百分比
              </button>
              <button
                @click="yAxisMode = 'value'"
                class="px-2.5 py-1 rounded-md text-[10px] font-bold transition-all cursor-pointer"
                :class="yAxisMode === 'value' ? 'bg-white dark:bg-slate-600 text-slate-800 dark:text-white shadow-sm' : 'text-slate-500 dark:text-slate-400 hover:text-slate-700'"
              >
                具体值
              </button>
            </div>

            <span class="text-[10px] text-slate-400 dark:text-slate-500 font-mono font-medium">
              <template v-if="isLoading">查询中...</template>
              <template v-else>
                可见序列 {{ chartGeometry.series.length }} · 数据点 {{ allTableRecords.length }} 个
                <button
                  v-if="isZoomed"
                  @click="zoomRange = null"
                  class="ml-2 px-2 py-0.5 rounded-md text-[10px] font-bold text-white bg-sky-500 hover:bg-sky-400 cursor-pointer inline-flex items-center gap-0.5 transition-colors"
                >
                  复位
                </button>
              </template>
            </span>
          </div>
        </div>

        <!-- 统计条 -->
        <div v-show="isDesktop || !isFullscreen" v-if="statsBySeries.length > 0" class="flex flex-wrap gap-x-5 gap-y-1 mb-3 pb-2 border-b border-slate-100 dark:border-slate-800/60 text-[10px] font-sans">
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

        <Teleport to="body" :disabled="!isFullscreen">
        <div
          class="w-full overflow-hidden"
          :class="isFullscreen ? 'fixed z-50 inset-0 bg-white dark:bg-slate-900 rounded-none border-0 p-3 flex flex-col' : 'relative bg-slate-50/50 dark:bg-slate-950/60 rounded-xl border border-slate-100 dark:border-slate-800 p-2'"
        >
          <div
            class="relative"
            :class="isFullscreen ? 'flex-1 min-h-0' : ''"
            :style="isMobileFullscreen ? { position: 'absolute', top: '50%', left: '50%', width: `${viewportH}px`, height: `${viewportW}px`, transform: 'translate(-50%, -50%) rotate(90deg)' } : {}"
          >
            <svg
              v-if="!chartGeometry.empty && chartGeometry.series.length >= 1"
              :viewBox="`0 0 ${SVG_W} ${SVG_H}`"
              preserveAspectRatio="xMidYMid meet"
              class="w-full min-w-0 block"
              :class="isFullscreen ? 'h-full' : 'h-auto'"
              :style="{ cursor: isDragging ? 'grabbing' : 'grab', touchAction: 'none' }"
              @mousemove="handleChartMouseMove"
              @mousedown="handleChartMouseDown"
              @mouseup="handleChartMouseUp"
              @mouseleave="chartMouseLeave"
              @wheel="handleChartWheel"
              @dblclick="handleChartDblClick"
              @touchstart="handleChartTouchStart"
              @touchmove="handleChartTouchMove"
              @touchend="handleChartTouchEnd"
              @touchcancel="handleChartTouchEnd"
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

              <!-- 时间方向标签：最新 / 最旧（随 xNewestFirst 自动对调） -->
              <g font-family="sans-serif" font-size="9" font-weight="bold">
                <!-- 最新侧：脉冲圆点 + 文字 -->
                <circle
                  :cx="xNewestFirst ? PAD_X : SVG_W - PAD_X"
                  :cy="10"
                  r="2.5"
                  fill="#1890ff"
                />
                <text
                  :x="xNewestFirst ? PAD_X + 7 : SVG_W - PAD_X - 7"
                  y="13"
                  fill="#1890ff"
                  :text-anchor="xNewestFirst ? 'start' : 'end'"
                >
                  最新
                </text>
                <!-- 最旧侧 -->
                <text
                  :x="xNewestFirst ? SVG_W - PAD_X : PAD_X"
                  y="13"
                  fill="#94a3b8"
                  :text-anchor="xNewestFirst ? 'end' : 'start'"
                >
                  最旧
                </text>
              </g>

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
                  <text
                    v-if="c.showLabel"
                    :x="c.x"
                    :y="c.y - 8"
                    :fill="s.color"
                    font-family="monospace"
                    font-size="8.5"
                    font-weight="bold"
                    text-anchor="middle"
                  >{{ c.v.toFixed(2) }}</text>
                </g>
                <!-- 最新数据点高亮（标记「哪边是最新」） -->
                <circle
                  v-if="s.latestPoint"
                  :cx="s.latestPoint.x"
                  :cy="s.latestPoint.y"
                  r="5.5"
                  fill="#1890ff"
                  :stroke="s.color"
                  stroke-width="2.5"
                />
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

              <!-- 框选矩形（Shift+拖动 放大） -->
              <rect
                v-if="dragState && dragState.type === 'brush'"
                :x="Math.min(dragState.startPx, brushEndPx)"
                :y="PAD_Y"
                :width="Math.abs(brushEndPx - dragState.startPx)"
                :height="SVG_H - 2 * PAD_Y"
                fill="#1890ff"
                fill-opacity="0.12"
                stroke="#1890ff"
                stroke-width="1"
                stroke-dasharray="4,3"
              />
            </svg>

            <!-- 空态 / 错误态 -->
            <div
              v-else
              class="py-16 text-center text-slate-400 dark:text-slate-500 flex flex-col items-center justify-center gap-2"
            >
              <Loader2 v-if="isLoading" class="w-8 h-8 text-slate-300 animate-spin" />
              <AlertCircle v-else class="w-8 h-8 text-slate-300 dark:text-slate-600 animate-bounce"
                :class="queryError ? 'text-rose-400 dark:text-rose-500' : ''" />
              <span class="text-xs" :class="queryError ? 'text-rose-500 dark:text-rose-400' : ''">
                <template v-if="selectedVars.length === 0">请先在左侧选择至少一个变量</template>
                <template v-else-if="isLoading">正在从时序库拉取数据...</template>
                <template v-else-if="queryError">{{ queryError }}</template>
                <template v-else>在选定的时间范围内，未查询到所选变量的任何时序。</template>
              </span>
              <button v-if="queryError && !isLoading" @click="executeHistoryQuery"
                class="mt-1 px-3 py-1 rounded-lg text-xs font-bold text-white bg-indigo-600 hover:bg-indigo-500 cursor-pointer inline-flex items-center gap-1">
                <RefreshCw class="w-3 h-3" /> 重试
              </button>
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

          <!-- 全屏：右上角悬浮退出按钮（放在旋转层之外，不随图表旋转） -->
          <button
            v-if="isFullscreen"
            @click="isFullscreen = false"
            class="absolute top-3 right-3 z-30 w-9 h-9 rounded-full bg-slate-800/80 hover:bg-slate-700 text-white shadow-lg cursor-pointer inline-flex items-center justify-center transition-colors"
            title="退出全屏"
          >
            <Minimize2 class="w-4 h-4" />
          </button>
        </div>
        </Teleport>
      </div>
    </div>

    <!-- 明细表格 -->
    <div v-show="mobileView === 'table'" class="px-3 sm:px-6 pb-6 select-none text-left flex-1 min-h-[300px] flex">
      <div class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden flex flex-col justify-between transition-colors">

        <!-- 移动端视图模式切换（仅手机端显示） -->
        <div
          class="md:hidden flex items-center justify-between px-3.5 py-2.5 border-b border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/60 shrink-0">
          <span class="text-xs font-bold text-slate-500 dark:text-slate-400 flex items-center gap-1.5">
            <FileSpreadsheet class="w-3.5 h-3.5" />
            数据明细（共 {{ totalRecordsCount }} 条）
          </span>
          <div class="flex items-center p-0.5 rounded-lg bg-slate-100 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shrink-0">
            <button type="button" @click="setMobileViewMode('card')"
              class="px-2 py-1 rounded-md text-xs font-bold flex items-center gap-1 transition-all cursor-pointer"
              :class="mobileViewMode === 'card'
                ? 'bg-white dark:bg-slate-900 text-[#1890ff] shadow-xs'
                : 'text-slate-400 hover:text-slate-600 dark:hover:text-slate-200'" title="卡片模式">
              <LayoutList class="w-3.5 h-3.5" />
              <span>卡片</span>
            </button>
            <button type="button" @click="setMobileViewMode('compact')"
              class="px-2 py-1 rounded-md text-xs font-bold flex items-center gap-1 transition-all cursor-pointer"
              :class="mobileViewMode === 'compact'
                ? 'bg-white dark:bg-slate-900 text-[#1890ff] shadow-xs'
                : 'text-slate-400 hover:text-slate-600 dark:hover:text-slate-200'" title="紧凑模式">
              <AlignJustify class="w-3.5 h-3.5" />
              <span>紧凑</span>
            </button>
          </div>
        </div>

        <!-- ================= 1. 桌面端表格（>= md） ================= -->
        <div class="hidden md:block overflow-x-auto flex-1">
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
                <td class="p-3.5 pr-5 text-right font-mono text-slate-400 dark:text-slate-500">{{ fmtTime(rec.timestamp) }}</td>
              </tr>

              <tr v-if="allTableRecords.length === 0">
                <td colspan="7" class="text-center py-16 text-slate-400 dark:text-slate-500">
                  没有符合检索过滤条件的物标时序块。
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- ================= 2. 移动端卡片模式（< md） ================= -->
        <div v-if="mobileViewMode === 'card'" class="md:hidden flex-1 overflow-y-auto">
          <div v-if="allTableRecords.length === 0"
            class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
            <AlertCircle class="w-8 h-8 text-slate-300 dark:text-slate-600" />
            <p class="text-xs font-sans">没有符合检索过滤条件的物标时序块。</p>
          </div>

          <div v-else class="p-3 space-y-2.5">
            <div v-for="rec in paginatedTableRecords" :key="'card-' + rec.id" @click="openRecordDetail(rec)"
              class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-3 shadow-2xs space-y-2 text-left transition-all active:bg-slate-50 dark:active:bg-slate-800/80 cursor-pointer">
              <!-- 卡片头：变量名 + 时间 -->
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0">
                  <div class="text-xs font-bold text-slate-800 dark:text-slate-100 truncate">{{ rec.variableName }}</div>
                  <div class="text-[10px] text-slate-400 font-mono truncate mt-0.5">{{ rec.variableKey }}</div>
                </div>
                <span class="text-[10px] font-mono text-slate-400 shrink-0">{{ fmtTime(rec.timestamp).slice(5) }}</span>
              </div>

              <!-- 实测值 -->
              <div class="flex items-end justify-between">
                <div>
                  <span class="text-2xl font-bold font-mono text-indigo-600 dark:text-indigo-400">{{ rec.value }}</span>
                  <span v-if="rec.unit" class="ml-1 text-xs font-sans text-slate-400 dark:text-slate-500">{{ rec.unit }}</span>
                </div>
                <span v-if="rec.quality && rec.quality !== 'Good'"
                  class="text-[9px] font-bold px-1.5 py-0.5 rounded-md bg-red-50 dark:bg-red-950/50 text-red-500">
                  {{ rec.quality }}
                </span>
                <span v-else class="text-[9px] text-slate-400 font-mono">Good</span>
              </div>

              <!-- 卡片底：设备 -->
              <div class="pt-2 border-t border-slate-100 dark:border-slate-800/80 flex items-center justify-between text-[10px] text-slate-500 dark:text-slate-400">
                <span class="truncate">{{ rec.deviceName }}<span class="font-mono text-slate-400 ml-1">{{ rec.deviceKey }}</span></span>
                <ChevronRight class="w-3.5 h-3.5 text-slate-300 dark:text-slate-600 shrink-0" />
              </div>
            </div>
          </div>
        </div>

        <!-- ================= 3. 移动端紧凑模式（< md） ================= -->
        <div v-else class="md:hidden flex-1 overflow-y-auto">
          <div v-if="allTableRecords.length === 0"
            class="flex flex-col items-center justify-center text-slate-400 dark:text-slate-500 py-16 gap-2">
            <AlertCircle class="w-8 h-8 text-slate-300 dark:text-slate-600" />
            <p class="text-xs font-sans">没有符合检索过滤条件的物标时序块。</p>
          </div>

          <div v-else class="p-2 space-y-1.5">
            <div v-for="rec in paginatedTableRecords" :key="'compact-' + rec.id" @click="openRecordDetail(rec)"
              class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-3 py-2 shadow-2xs flex items-center justify-between gap-2.5 active:bg-slate-50 dark:active:bg-slate-800/80 cursor-pointer transition-colors text-left">
              <div class="flex items-center gap-2 min-w-0 flex-1">
                <div class="min-w-0 flex-1">
                  <div class="text-xs text-slate-800 dark:text-slate-100 truncate font-medium">
                    {{ rec.variableName }}
                    <span v-if="rec.unit" class="text-[9px] text-slate-400 font-sans">{{ rec.unit }}</span>
                  </div>
                  <div class="text-[10px] text-slate-400 flex items-center gap-1.5 truncate mt-0.5">
                    <span class="text-sky-600 dark:text-sky-400 font-semibold">{{ rec.deviceName }}</span>
                    <span v-if="rec.quality && rec.quality !== 'Good'" class="text-red-500 font-bold">{{ rec.quality }}</span>
                    <span v-else>· Good</span>
                  </div>
                </div>
              </div>
              <div class="text-right shrink-0 flex items-center gap-1.5">
                <div>
                  <div class="text-sm font-bold font-mono text-indigo-600 dark:text-indigo-400">{{ rec.value }}</div>
                  <div class="text-[10px] font-mono text-slate-400">{{ fmtTime(rec.timestamp).slice(11, 19) }}</div>
                </div>
                <ChevronRight class="w-3.5 h-3.5 text-slate-300 dark:text-slate-600" />
              </div>
            </div>
          </div>
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

    <!-- ================= 移动端底部详情抽屉 (Bottom Sheet) ================= -->
    <div v-if="selectedRecordDetail" class="fixed inset-0 z-50 overflow-hidden select-none">
      <!-- 背景遮罩 -->
      <div class="absolute inset-0 bg-slate-900/60 backdrop-blur-xs transition-opacity" @click="closeRecordDetail" />

      <!-- 抽屉内容容器 -->
      <div
        class="fixed inset-x-0 bottom-0 z-50 max-h-[88vh] bg-white dark:bg-slate-900 rounded-t-2xl shadow-2xl flex flex-col overflow-hidden text-left border-t border-slate-200 dark:border-slate-800 animate-in slide-in-from-bottom duration-200">
        <!-- 抽屉顶部拖动条 -->
        <div class="w-12 h-1 bg-slate-300 dark:bg-slate-700 rounded-full mx-auto mt-2.5 mb-1" />

        <!-- 抽屉标题栏 -->
        <div class="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between shrink-0">
          <div class="flex items-center gap-2 min-w-0">
            <span class="text-xs font-bold text-slate-900 dark:text-white truncate">{{ selectedRecordDetail.variableName }}</span>
          </div>
          <button type="button" @click="closeRecordDetail"
            class="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer">
            <X class="w-5 h-5" />
          </button>
        </div>

        <!-- 抽屉主体内容 -->
        <div class="p-4 overflow-y-auto space-y-3.5 text-xs">
          <!-- 实测值高亮 -->
          <div class="rounded-xl bg-indigo-50 dark:bg-indigo-950/30 border border-indigo-100 dark:border-indigo-900/40 p-3.5 flex items-end justify-between">
            <div>
              <span class="text-slate-400 dark:text-slate-500 block text-[10px]">核算实测值</span>
              <span class="text-3xl font-bold font-mono text-indigo-600 dark:text-indigo-400">{{ selectedRecordDetail.value }}</span>
              <span v-if="selectedRecordDetail.unit" class="ml-1.5 text-sm font-sans text-slate-500 dark:text-slate-400">{{ selectedRecordDetail.unit }}</span>
            </div>
            <span v-if="selectedRecordDetail.quality && selectedRecordDetail.quality !== 'Good'"
              class="text-[10px] font-bold px-2 py-0.5 rounded-md bg-red-50 dark:bg-red-950/50 text-red-500">
              {{ selectedRecordDetail.quality }}
            </span>
            <span v-else class="text-[10px] text-slate-400 font-mono">Good</span>
          </div>

          <!-- 结构化关键信息 -->
          <div class="grid grid-cols-2 gap-2.5 bg-slate-50 dark:bg-slate-950 p-3 rounded-xl border border-slate-200/80 dark:border-slate-800 text-[11px]">
            <div>
              <span class="text-slate-400 block text-[10px]">采集项 ID</span>
              <span class="font-mono font-medium text-slate-800 dark:text-slate-200">{{ selectedRecordDetail.id }}</span>
            </div>
            <div>
              <span class="text-slate-400 block text-[10px]">采样时间</span>
              <span class="font-mono font-medium text-slate-800 dark:text-slate-200">{{ fmtTime(selectedRecordDetail.timestamp) }}</span>
            </div>
            <div class="col-span-2">
              <span class="text-slate-400 block text-[10px]">所属设备</span>
              <span class="font-medium text-slate-800 dark:text-slate-200">{{ selectedRecordDetail.deviceName }}
                <span class="font-mono text-slate-400">({{ selectedRecordDetail.deviceKey }})</span>
              </span>
            </div>
            <div class="col-span-2">
              <span class="text-slate-400 block text-[10px]">变量键名</span>
              <span class="font-mono font-medium text-slate-800 dark:text-slate-200">{{ selectedRecordDetail.variableKey }}</span>
            </div>
            <div class="col-span-2">
              <span class="text-slate-400 block text-[10px]">物标测位中文注释</span>
              <span class="font-medium text-slate-800 dark:text-slate-200">{{ selectedRecordDetail.variableName }}</span>
            </div>
          </div>
        </div>

        <!-- 抽屉操作底栏 -->
        <div class="p-3 bg-slate-50 dark:bg-slate-950 border-t border-slate-200 dark:border-slate-800 flex gap-2 shrink-0">
          <button type="button" @click="closeRecordDetail"
            class="flex-1 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-700 text-white font-bold text-xs cursor-pointer shadow-xs transition-colors">
            关闭
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
