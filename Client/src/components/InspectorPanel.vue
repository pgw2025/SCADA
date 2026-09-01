<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { HMIComponent, ScadaPage, PageBackground, PageBackgroundType, HMILayer, HmiMenuItem, HmiDashboardItem, HmiTrendSeries } from '../types';
import { devices } from '../store/deviceStore';
import { desktopPages, mobilePages, currentPlatform } from '../store/scadaStore';
import { systemScripts } from '../store/configStore';
import { loadSystemScripts } from '../services/scriptService';
import { loginUser } from '../store/userStore';
import { ROLE_ADMIN } from '../constants/roles';
import { getWidgetDef, MENU_ICON_OPTIONS, getMenuIcon, TREND_SERIES_PALETTE } from '../widgetRegistry';
import { Settings, Tag, Sliders, Layout, Hash, ChevronRight, Palette, Expand, Layers, Eye, EyeOff, Lock, Unlock, Plus, Trash2, ChevronUp, ChevronDown, LayoutDashboard, Grid, Columns, Table, Sparkles } from 'lucide-vue-next';
import ImageLibraryDialog from './ImageLibraryDialog.vue';

const props = defineProps<{
  selectedComponent: HMIComponent | null;
  currentPageId?: string;
  /** 背景选中态：非空时显示「页面属性」表单（背景 + 自适应屏幕配置） */
  backgroundPage?: ScadaPage | null;
  /** 当前页面的图层列表 */
  layers?: HMILayer[];
}>();

const emit = defineEmits<{
  (e: 'updateComponent', id: string, updates: Partial<HMIComponent>): void;
  (e: 'updatePage', updates: Partial<ScadaPage>): void;
  (e: 'collapse'): void;
}>();

const componentProps = computed(() => {
  return props.selectedComponent?.props ?? {};
});

// 当前类型注册表默认 props：回显缺省值与运行态兜底共用同一真相源
const typeDefaults = computed(() =>
  getWidgetDef(props.selectedComponent?.type ?? '')?.defaultProps() ?? {}
);

// 自定义画布尺寸：范围 200~10000，失焦/回车提交；非法值回退当前页面值
const CANVAS_SIZE_MIN = 200;
const CANVAS_SIZE_MAX = 10000;
const canvasW = ref<number>(props.backgroundPage?.width ?? 1100);
const canvasH = ref<number>(props.backgroundPage?.height ?? 700);
watch(
  () => [props.backgroundPage?.width, props.backgroundPage?.height] as const,
  ([w, h]) => {
    canvasW.value = w ?? 1100;
    canvasH.value = h ?? 700;
  }
);
const applyCanvasSize = () => {
  const page = props.backgroundPage;
  if (!page) return;
  const clamp = (v: number, fallback: number) => {
    const n = Math.round(Number(v));
    if (!Number.isFinite(n)) return fallback;
    return Math.min(CANVAS_SIZE_MAX, Math.max(CANVAS_SIZE_MIN, n));
  };
  const w = clamp(canvasW.value, page.width ?? 1100);
  const h = clamp(canvasH.value, page.height ?? 700);
  canvasW.value = w;
  canvasH.value = h;
  if (w !== page.width || h !== page.height) {
    emit('updatePage', { width: w, height: h });
  }
};

// Prop mutator helper - emits change upwards
const updateProp = (key: string, value: any) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    props: {
      ...componentProps.value,
      [key]: value,
    },
  });
};

// 批量 props 应用（预设风格一键切换）：单次 emit 合并提交，避免逐 key 连续 emit 的竞态
const applyProps = (patch: Record<string, any>) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    props: { ...componentProps.value, ...patch },
  });
};

// ===== 圆角按钮：5 种工业预设风格（启动/停止/复位/点动/急停）=====
// 预设只覆盖状态文案/配色/控制模式/边框，不改动绑定与尺寸；应用后仍可继续微调
const roundedBtnPresets: Record<string, Record<string, any>> = {
  start: {
    presetStyle: 'start', buttonMode: 'set-bit',
    state0Text: '待机', state0BgColor: '#334155', state0TextColor: '#94a3b8',
    state1Text: '运行中', state1BgColor: '#16a34a', state1TextColor: '#ffffff',
    strokeColor: '#16a34a', customStates: '',
  },
  stop: {
    presetStyle: 'stop', buttonMode: 'reset-bit',
    state0Text: '停止', state0BgColor: '#7f1d1d', state0TextColor: '#fca5a5',
    state1Text: '停止', state1BgColor: '#dc2626', state1TextColor: '#ffffff',
    strokeColor: '#dc2626', customStates: '',
  },
  reset: {
    presetStyle: 'reset', buttonMode: 'momentary',
    state0Text: '就绪', state0BgColor: '#1e3a8a', state0TextColor: '#93c5fd',
    state1Text: '复位中', state1BgColor: '#2563eb', state1TextColor: '#ffffff',
    strokeColor: '#2563eb', customStates: '',
  },
  jog: {
    presetStyle: 'jog', buttonMode: 'momentary',
    state0Text: '点动', state0BgColor: '#7c2d12', state0TextColor: '#fdba74',
    state1Text: '点动中', state1BgColor: '#ea580c', state1TextColor: '#ffffff',
    strokeColor: '#ea580c', customStates: '',
  },
  estop: {
    presetStyle: 'estop', buttonMode: 'set-bit', borderWidth: 3,
    state0Text: '急停', state0BgColor: '#991b1b', state0TextColor: '#fecaca',
    state1Text: '急停触发', state1BgColor: '#dc2626', state1TextColor: '#ffffff',
    strokeColor: '#f87171', customStates: '',
  },
};
const applyRoundedBtnPreset = (key: string) => {
  const preset = roundedBtnPresets[key];
  if (preset) applyProps(preset);
};

// run-script 模式：管理员编辑器内懒加载脚本列表供下拉选择（列表接口 RequireAdmin，
// 非管理员不自动拉取，避免无谓 403 噪音；运行态触发走 /api/ScriptRuntime 不依赖此列表）
const scriptListRequested = ref(false);
watch(
  () => props.selectedComponent?.type === 'rounded-btn' && props.selectedComponent?.props.buttonMode === 'run-script',
  (need) => {
    if (need && !scriptListRequested.value && loginUser.value?.role === ROLE_ADMIN) {
      scriptListRequested.value = true;
      loadSystemScripts().catch(() => { scriptListRequested.value = false; });
    }
  },
  { immediate: true }
);

// ===== 圆角按钮：操作变量绑定（写入目标，可与显示/背景变量分离）=====
// 未配置时回落主绑定（bindDeviceId/bindVariableKey）
const opBindingVariableOptions = computed(() => {
  const dev = devices.value.find((d) => String(d.id) === String(componentProps.value.opDeviceId));
  if (dev && dev.variables) {
    return Object.keys(dev.variables).map((k) => ({ key: k }));
  }
  return [];
});

const onOpDeviceChange = (val: string) => {
  applyProps({ opDeviceId: val === '' ? null : Number(val), opVariableKey: '' });
};

const onOpVariableChange = (val: string) => {
  updateProp('opVariableKey', val);
};

const updateComponentField = (field: keyof HMIComponent, value: any) => {
  if (!props.selectedComponent) return;
  emit('updateComponent', props.selectedComponent.id, {
    [field]: value,
  });
};

// 解析数值输入：合法（含 0）原样写入，非法（NaN/空）回退缺省值。
// 修复「threshold 填 0 被写成 90/10」类问题。
const numInput = (raw: string, fallback: number): number => {
  const n = parseFloat(raw);
  return Number.isFinite(n) ? n : fallback;
};

// 阶段3：复合绑定（设备 + 变量）两级选择
const bindingVariableOptions = computed(() => {
  const dev = devices.value.find((d) => String(d.id) === String(props.selectedComponent?.bindDeviceId));
  if (dev && dev.variables) {
    return Object.keys(dev.variables).map((k) => ({ key: k }));
  }
  // 严格模式：必须先选设备，禁止裸 key 汇总全部变量键
  return [];
});

const onBindDeviceChange = (val: string) => {
  const id = val === '' ? null : Number(val);
  updateComponentField('bindDeviceId', id);
  updateComponentField('bindVariableKey', ''); // 设备变更后清空变量
  updateComponentField('bindField', ''); // 同时清除遗留 bindField，防止运行态拿旧值下发写指令
};

const onBindVariableChange = (val: string) => {
  updateComponentField('bindVariableKey', val);
  updateComponentField('bindField', val); // 同步遗留字段，兼容旧逻辑/HMIWidget 提示
};

// 阶段3：导航目标候选（仅限当前端画面，排除自身）。编辑器内按 currentPlatform 过滤，
// 保证「跨端跳转不允许」由设计约束（目标下拉不含异端画面）。
// value 用稳定引用：已落库存 `srv-{serverId}`（跨会话可比）；未落库页面暂存本地 id，
// 由跳转侧 normalizePageRef 兜底比较。
const navTargetOptions = computed(() => {
  const list = currentPlatform.value === 'Mobile' ? mobilePages.value : desktopPages.value;
  return list
    // 排除「当前页面」本身：页面 id 与组件 id 不可比，须用父级传入的 currentPageId
    .filter(p => p.id !== props.currentPageId)
    .map(p => ({ id: p.serverId ? `srv-${p.serverId}` : p.id, name: p.name }));
});

// ===== nav-menu 菜单项编辑器：3~5 项，图标/文字/跳转目标，支持增删与上下排序 =====
const MENU_ITEM_MIN = 3;
const MENU_ITEM_MAX = 5;

const menuItems = computed<HmiMenuItem[]>(() => {
  const raw = componentProps.value.menuItems;
  return Array.isArray(raw) ? (raw as HmiMenuItem[]) : [];
});

// 整体替换式提交（menuItems 是数组 prop，须整体写入不可局部 mutate）
const commitMenuItems = (items: HmiMenuItem[]) => updateProp('menuItems', items);

const updateMenuItem = (index: number, patch: Partial<HmiMenuItem>) => {
  const next = menuItems.value.map((it, i) => (i === index ? { ...it, ...patch } : it));
  commitMenuItems(next);
};

const addMenuItem = () => {
  if (menuItems.value.length >= MENU_ITEM_MAX) return;
  commitMenuItems([...menuItems.value, { icon: 'settings', text: `菜单 ${menuItems.value.length + 1}`, targetPageId: null }]);
};

const removeMenuItem = (index: number) => {
  if (menuItems.value.length <= MENU_ITEM_MIN) return;
  commitMenuItems(menuItems.value.filter((_, i) => i !== index));
};

const moveMenuItem = (index: number, dir: -1 | 1) => {
  const to = index + dir;
  if (to < 0 || to >= menuItems.value.length) return;
  const next = [...menuItems.value];
  [next[index], next[to]] = [next[to], next[index]];
  commitMenuItems(next);
};

// 图标选择网格的展开项（-1 = 全部收起；同一时间只展开一项）
const openIconPickerIndex = ref(-1);

// ===== multi-var-dashboard 多变量监控看板配置辅助函数 =====
const dashboardItems = computed<HmiDashboardItem[]>(() => {
  const raw = componentProps.value.dashboardItems;
  return Array.isArray(raw) ? (raw as HmiDashboardItem[]) : [];
});

const commitDashboardItems = (items: HmiDashboardItem[]) => updateProp('dashboardItems', items);

const updateDashboardItem = (index: number, patch: Partial<HmiDashboardItem>) => {
  const next = dashboardItems.value.map((it, i) => (i === index ? { ...it, ...patch } : it));
  commitDashboardItems(next);
};

const addDashboardItem = () => {
  const devId = props.selectedComponent?.bindDeviceId ?? devices.value[0]?.id ?? null;
  const dev = devices.value.find(d => d.id === devId) || devices.value[0];
  const keys = dev ? Object.keys(dev.variables || {}) : [];
  const existingKeys = new Set(dashboardItems.value.map(it => it.variableKey));
  const unusedKey = keys.find(k => !existingKeys.has(k)) || keys[0] || 'var_1';
  const meta = dev?.variableMeta?.[unusedKey];

  const newItem: HmiDashboardItem = {
    id: `item-${Date.now()}-${dashboardItems.value.length + 1}`,
    deviceId: dev?.id ?? null,
    variableKey: unusedKey,
    label: meta?.name || unusedKey,
    unit: meta?.unit || '',
    precision: typeof dev?.variables?.[unusedKey] === 'number' ? 1 : null,
    showStatusDot: true,
    thresholdMin: null,
    thresholdMax: null,
  };
  commitDashboardItems([...dashboardItems.value, newItem]);
};

const removeDashboardItem = (index: number) => {
  commitDashboardItems(dashboardItems.value.filter((_, i) => i !== index));
};

const moveDashboardItem = (index: number, dir: -1 | 1) => {
  const to = index + dir;
  if (to < 0 || to >= dashboardItems.value.length) return;
  const next = [...dashboardItems.value];
  [next[index], next[to]] = [next[to], next[index]];
  commitDashboardItems(next);
};

// 一键从所选设备导入所有变量
const importAllVariablesFromDevice = (targetDevId?: number | null) => {
  const devId = targetDevId ?? props.selectedComponent?.bindDeviceId ?? devices.value[0]?.id;
  const dev = devices.value.find(d => d.id === devId);
  if (!dev || !dev.variables) return;

  const newItems: HmiDashboardItem[] = Object.keys(dev.variables).map((k, idx) => {
    const meta = dev.variableMeta?.[k];
    const isNum = typeof dev.variables[k] === 'number';
    return {
      id: `item-${dev.id}-${k}-${Date.now()}-${idx}`,
      deviceId: dev.id,
      variableKey: k,
      label: meta?.name || k,
      unit: meta?.unit || '',
      precision: isNum ? 2 : null,
      showStatusDot: true,
      thresholdMin: null,
      thresholdMax: null,
    };
  });

  commitDashboardItems(newItems);
};

// 获取某个监控项对应设备下的变量选项
const getItemVariableOptions = (itemDevId?: number | null) => {
  const devId = itemDevId != null ? itemDevId : (props.selectedComponent?.bindDeviceId ?? devices.value[0]?.id);
  const dev = devices.value.find(d => d.id === devId) || devices.value[0];
  if (!dev || !dev.variables) return [];
  return Object.keys(dev.variables).map(k => ({
    key: k,
    name: dev.variableMeta?.[k]?.name || k,
    unit: dev.variableMeta?.[k]?.unit || '',
    type: typeof dev.variables[k] === 'number' ? 'analog' : 'digital'
  }));
};

// ===== 趋势图多序列（trend-chart）编辑器逻辑：镜像 dashboard 子项编辑器 =====
const trendSeries = computed<HmiTrendSeries[]>(() => {
  const raw = componentProps.value.trendSeries;
  return Array.isArray(raw) ? (raw as HmiTrendSeries[]) : [];
});

const commitTrendSeries = (items: HmiTrendSeries[]) => updateProp('trendSeries', items);

const updateTrendSeries = (index: number, patch: Partial<HmiTrendSeries>) => {
  const next = trendSeries.value.map((it, i) => (i === index ? { ...it, ...patch } : it));
  commitTrendSeries(next);
};

const addTrendSeries = () => {
  const devId = props.selectedComponent?.bindDeviceId ?? devices.value[0]?.id ?? null;
  const dev = devices.value.find((d) => d.id === devId) || devices.value[0];
  const keys = dev ? Object.keys(dev.variables || {}) : [];
  const existingKeys = new Set(trendSeries.value.map((it) => `${it.deviceId ?? ''}:${it.variableKey}`));
  const unusedKey = keys.find((k) => !existingKeys.has(`${dev?.id ?? ''}:${k}`)) || keys[0] || 'var_1';
  const meta = dev?.variableMeta?.[unusedKey];
  const color = TREND_SERIES_PALETTE[trendSeries.value.length % TREND_SERIES_PALETTE.length];
  const newItem: HmiTrendSeries = {
    id: `series-${Date.now()}-${trendSeries.value.length + 1}`,
    deviceId: dev?.id ?? null,
    variableKey: unusedKey,
    label: meta?.name || unusedKey,
    unit: meta?.unit || '',
    color,
    lineWidth: 2,
    minValue: null,
    maxValue: null,
    precision: typeof dev?.variables?.[unusedKey] === 'number' ? 1 : null,
    thresholdMin: null,
    thresholdMax: null,
  };
  commitTrendSeries([...trendSeries.value, newItem]);
};

const removeTrendSeries = (index: number) =>
  commitTrendSeries(trendSeries.value.filter((_, i) => i !== index));

const moveTrendSeries = (index: number, dir: -1 | 1) => {
  const to = index + dir;
  if (to < 0 || to >= trendSeries.value.length) return;
  const next = [...trendSeries.value];
  [next[index], next[to]] = [next[to], next[index]];
  commitTrendSeries(next);
};

const importAllVariablesForTrend = (targetDevId?: number | null) => {
  const devId = targetDevId ?? props.selectedComponent?.bindDeviceId ?? devices.value[0]?.id;
  const dev = devices.value.find((d) => d.id === devId);
  if (!dev || !dev.variables) return;
  const newItems: HmiTrendSeries[] = Object.keys(dev.variables).map((k, idx) => {
    const meta = dev.variableMeta?.[k];
    const isNum = typeof dev.variables[k] === 'number';
    return {
      id: `series-${dev.id}-${k}-${Date.now()}-${idx}`,
      deviceId: dev.id,
      variableKey: k,
      label: meta?.name || k,
      unit: meta?.unit || '',
      color: TREND_SERIES_PALETTE[idx % TREND_SERIES_PALETTE.length],
      lineWidth: 2,
      minValue: null,
      maxValue: null,
      precision: isNum ? 2 : null,
      thresholdMin: null,
      thresholdMax: null,
    };
  });
  commitTrendSeries(newItems);
};

// D4-A：选中趋势图且尚无序列、但有旧式单绑定时，一次性迁移为第 1 条序列（向后兼容）
watch(
  () => props.selectedComponent,
  (c) => {
    if (c?.type !== 'trend-chart') return;
    const existing = Array.isArray(c.props?.trendSeries) ? c.props.trendSeries : [];
    if (existing.length === 0 && c.bindDeviceId != null && c.bindVariableKey) {
      commitTrendSeries([{
        id: 'legacy',
        deviceId: c.bindDeviceId,
        variableKey: c.bindVariableKey,
        label: c.bindVariableKey,
        color: TREND_SERIES_PALETTE[0],
        lineWidth: 2,
        minValue: null,
        maxValue: null,
        precision: null,
        thresholdMin: null,
        thresholdMax: null,
      }]);
    }
  },
  { immediate: true }
);

// ===== 页面属性（背景 + 自适应屏幕）=====
// 未配置背景时的默认值（纯色白底）；每次编辑整体提交，父级负责落库
const pageBackground = computed<PageBackground>(() =>
  props.backgroundPage?.background ?? { type: 'color', color: '#ffffff' });

const updateBackground = (patch: Partial<PageBackground>) => {
  emit('updatePage', { background: { ...pageBackground.value, ...patch } });
};

const onBackgroundTypeChange = (val: string) => {
  const type = val as PageBackgroundType;
  // 切换类型时保留各类型已有参数，仅补默认值，避免来回切换丢失已填内容
  const cur = pageBackground.value;
  const patch: Partial<PageBackground> = { type };
  if (type === 'color' && !cur.color) patch.color = '#ffffff';
  if (type === 'gradient') {
    if (!cur.gradientStart) patch.gradientStart = '#e0f2fe';
    if (!cur.gradientEnd) patch.gradientEnd = '#1e3a8a';
    if (typeof cur.gradientAngle !== 'number') patch.gradientAngle = 180;
  }
  if (type === 'image') {
    if (!cur.imageFit) patch.imageFit = 'fill';
  }
  updateBackground(patch);
};

const onAdaptModeChange = (val: string) => {
  emit('updatePage', { adaptMode: val === 'FitScaleUp' || val === 'Stretch' ? val : null });
};

// ===== 图片图元 / 背景图：图库选图 =====
// 图元换图（updateProp 走既有防抖落库链路）
const showImagePicker = ref(false);
const onPickComponentImage = (img: { url: string }) => {
  showImagePicker.value = false;
  updateProp('imageUrl', img.url);
};

// 背景选图（updateBackground 整体提交，父级落库）
const showBgImagePicker = ref(false);
const onPickBackgroundImage = (img: { url: string }) => {
  showBgImagePicker.value = false;
  updateBackground({ imageUrl: img.url });
};

// 5 套主题风格适配的画布背景快速配色预设
const THEME_CANVAS_PRESETS = [
  {
    id: 'pure-white',
    name: '极简亮白',
    category: '☀️ 浅色大方',
    isLight: true,
    color: '#ffffff',
    borderColor: '#e2e8f0',
    gradient: { start: '#ffffff', end: '#f1f5f9', angle: 180 },
    textColor: '#0f172a',
    accentColor: '#2563eb',
  },
  {
    id: 'titanium-light',
    name: '工业钛灰',
    category: '☀️ 浅色大方',
    isLight: true,
    color: '#f1f5f9',
    borderColor: '#cbd5e1',
    gradient: { start: '#f8fafc', end: '#e2e8f0', angle: 180 },
    textColor: '#1e293b',
    accentColor: '#0284c7',
  },
  {
    id: 'slate-dark',
    name: '经典石板深灰',
    category: '🌙 深色稳健',
    isLight: false,
    color: '#0f172a',
    borderColor: '#334155',
    gradient: { start: '#1e293b', end: '#0f172a', angle: 180 },
    textColor: '#f8fafc',
    accentColor: '#38bdf8',
  },
  {
    id: 'navy-midnight',
    name: '深海商务暗蓝',
    category: '🌙 深色稳健',
    isLight: false,
    color: '#061426',
    borderColor: '#1e293b',
    gradient: { start: '#0b172a', end: '#061426', angle: 180 },
    textColor: '#ffffff',
    accentColor: '#38bdf8',
  },
  {
    id: 'translucent-frost',
    name: '悬浮通透暗调',
    category: '🌿 轻量通透',
    isLight: false,
    color: '#111c2e',
    borderColor: 'rgba(255,255,255,0.2)',
    gradient: { start: '#1e293b', end: '#0a0f1d', angle: 180 },
    textColor: '#ffffff',
    accentColor: '#38bdf8',
  },
];

// 一键应用 5 套主题对应的画布背景
const applyThemePreset = (preset: typeof THEME_CANVAS_PRESETS[0]) => {
  if (pageBackground.value.type === 'gradient') {
    updateBackground({
      gradientStart: preset.gradient.start,
      gradientEnd: preset.gradient.end,
      gradientAngle: preset.gradient.angle,
    });
  } else {
    updateBackground({
      type: 'color',
      color: preset.color,
    });
  }
};
</script>

<template>
  <!-- 空态：未选中任何元件/背景 -->
  <div v-if="!selectedComponent && !backgroundPage"
    class="h-full bg-[#fafafa] dark:bg-slate-950 p-6 text-gray-400 dark:text-slate-500 text-xs flex flex-col justify-between items-center text-center transition-colors relative">
    <div class="w-full flex justify-end">
      <button @click="emit('collapse')"
        class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
        title="收起属性面板">
        <ChevronRight class="w-4 h-4" />
      </button>
    </div>
    <div class="flex flex-col items-center justify-center my-auto">
      <!-- Spinning Cog -->
      <Settings class="w-8 h-8 text-[#1890ff] dark:text-sky-400 mb-2 animate-spin-slow opacity-60" />
      <p class="font-semibold text-gray-700 dark:text-slate-300">属性面板</p>
      <p class="text-[10px] text-gray-400 dark:text-slate-500 mt-2.5 max-w-[200px] leading-relaxed">
        请在画布上选择元件以配置属性。<br />点击画布空白背景可配置页面属性。
      </p>
    </div>
    <div class="h-4"></div>
  </div>

  <!-- 页面属性：点击画布背景后显示（背景设置 + 自适应屏幕设置） -->
  <div v-else-if="backgroundPage"
    class="h-full flex flex-col bg-white dark:bg-slate-900 text-[#262626] dark:text-slate-100 overflow-y-auto transition-colors">
    <!-- Title -->
    <div
      class="p-4 border-b border-[#f0f0f0] dark:border-slate-800 bg-[#fafafa] dark:bg-slate-950 flex items-center justify-between">
      <div class="flex items-center gap-2">
        <Palette class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
        <h3 class="text-xs font-bold text-[#141414] dark:text-slate-100 uppercase tracking-wider">
          页面属性
        </h3>
      </div>
      <button @click="emit('collapse')"
        class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
        title="收起属性面板">
        <ChevronRight class="w-4 h-4" />
      </button>
    </div>

    <div class="p-4 space-y-4 text-left">
      <!-- 页面基本信息（只读） -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Layout class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          基本信息
        </div>
        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">画面名称</label>
            <input type="text" disabled :value="backgroundPage.name"
              class="w-full bg-[#fafafa] dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 mt-0.5 text-gray-400 dark:text-slate-500 cursor-not-allowed" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">画布尺寸 (px)</label>
            <div class="flex items-center gap-1 mt-0.5">
              <input type="number" min="200" max="10000" step="10" v-model.number="canvasW" @change="applyCanvasSize"
                @keyup.enter="applyCanvasSize"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2 py-1.5 font-mono text-[10px] text-[#262626] dark:text-white focus:outline-none"
                title="画布宽度（200~10000）" />
              <span class="text-gray-400 text-[10px] shrink-0">×</span>
              <input type="number" min="200" max="10000" step="10" v-model.number="canvasH" @change="applyCanvasSize"
                @keyup.enter="applyCanvasSize"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2 py-1.5 font-mono text-[10px] text-[#262626] dark:text-white focus:outline-none"
                title="画布高度（200~10000）" />
            </div>
          </div>
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800 my-4" />

      <!-- 背景设置 -->
      <section class="space-y-3">
        <div class="flex items-center justify-between text-xs font-semibold text-gray-700 dark:text-slate-300">
          <div class="flex items-center gap-1.5">
            <Palette class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
            背景设置
          </div>
          <span class="text-[10px] text-gray-400 dark:text-slate-500 font-normal">支持5大主题一键适配</span>
        </div>

        <!-- 5 套主题风格快速套用卡片 -->
        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400 flex items-center justify-between">
            <span>主题风格预设 (5大体系)</span>
            <span class="text-[9px] text-[#1890ff] dark:text-sky-400">点击即应用</span>
          </label>
          <div class="grid grid-cols-1 gap-1.5 mt-1.5">
            <div v-for="t in THEME_CANVAS_PRESETS" :key="t.id" @click="applyThemePreset(t)"
              class="flex items-center justify-between px-2.5 py-1.5 rounded-lg border text-xs cursor-pointer transition-all hover:scale-[1.01] active:scale-[0.99] shadow-xs select-none"
              :class="((pageBackground.type === 'color' && pageBackground.color === t.color) || (pageBackground.type === 'gradient' && pageBackground.gradientStart === t.gradient.start && pageBackground.gradientEnd === t.gradient.end)) ? 'ring-2 ring-[#1890ff] dark:ring-sky-400 border-transparent' : 'border-gray-200 dark:border-slate-700 hover:border-[#1890ff] dark:hover:border-sky-500'"
              :style="{
                background: t.isLight ? t.color : t.color,
                color: t.textColor,
                border: `1px solid ${t.borderColor}`
              }">
              <div class="flex items-center gap-2 min-w-0">
                <span class="w-3 h-3 rounded-full shrink-0 border border-black/20"
                  :style="{ background: t.accentColor }" />
                <div class="truncate">
                  <span class="font-bold text-[11px]">{{ t.name }}</span>
                  <span class="text-[9px] opacity-60 ml-1.5">{{ t.category }}</span>
                </div>
              </div>
              <!-- 纯色/渐变微缩色块 -->
              <div class="flex items-center gap-1 shrink-0">
                <span class="text-[9px] font-mono opacity-70">{{ t.color }}</span>
                <div class="w-5 h-3.5 rounded border border-black/20" :style="{
                  backgroundImage: `linear-gradient(135deg, ${t.gradient.start}, ${t.gradient.end})`
                }" :title="`渐变: ${t.gradient.start} ➔ ${t.gradient.end}`" />
              </div>
            </div>
          </div>
        </div>

        <div class="border-t border-dashed border-gray-200 dark:border-slate-800 my-2" />

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">背景类型</label>
          <select :value="pageBackground.type"
            @change="onBackgroundTypeChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="color">纯色背景 (Solid Color)</option>
            <option value="gradient">渐变背景 (Linear Gradient)</option>
            <option value="image">图片背景 (Image URL)</option>
          </select>
        </div>

        <!-- 纯色 -->
        <template v-if="pageBackground.type === 'color'">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">自定义颜色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="pageBackground.color || '#ffffff'"
                @input="updateBackground({ color: ($event.target as HTMLInputElement).value })"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="pageBackground.color || '#ffffff'"
                @input="updateBackground({ color: ($event.target as HTMLInputElement).value })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>

          <!-- 5套主题适配颜色快速选择 -->
          <div class="space-y-2">
            <div>
              <div class="flex items-center justify-between text-[10px] text-gray-500 dark:text-slate-400">
                <span>☀️ 浅色主题底色 (Light Presets)</span>
              </div>
              <div class="grid grid-cols-4 gap-1.5 mt-1">
                <button v-for="item in [
                  { color: '#ffffff', name: '极简纯白' },
                  { color: '#f8fafc', name: '冷光亮白' },
                  { color: '#f1f5f9', name: '工业钛灰' },
                  { color: '#e2e8f0', name: '金属中灰' }
                ]" :key="item.color" @click="updateBackground({ color: item.color })"
                  class="h-7 rounded-md border flex flex-col items-center justify-center cursor-pointer transition-all hover:scale-105 active:scale-95 shadow-2xs"
                  :class="pageBackground.color === item.color ? 'ring-2 ring-[#1890ff] dark:ring-sky-400 border-transparent' : 'border-gray-300 dark:border-slate-700'"
                  :style="{ backgroundColor: item.color }" :title="`${item.name} (${item.color})`">
                  <span class="text-[8px] font-mono font-medium text-slate-800 leading-none">{{ item.name }}</span>
                </button>
              </div>
            </div>

            <div>
              <div class="flex items-center justify-between text-[10px] text-gray-500 dark:text-slate-400">
                <span>🌙 深色/通透主题底色 (Dark & Frost Presets)</span>
              </div>
              <div class="grid grid-cols-4 gap-1.5 mt-1">
                <button v-for="item in [
                  { color: '#0f172a', name: '石板深灰' },
                  { color: '#1e293b', name: '石板中黑' },
                  { color: '#061426', name: '深海暗蓝' },
                  { color: '#111c2e', name: '通透暗调' }
                ]" :key="item.color" @click="updateBackground({ color: item.color })"
                  class="h-7 rounded-md border flex flex-col items-center justify-center cursor-pointer transition-all hover:scale-105 active:scale-95 shadow-2xs"
                  :class="pageBackground.color === item.color ? 'ring-2 ring-[#1890ff] dark:ring-sky-400 border-transparent' : 'border-gray-400 dark:border-slate-700'"
                  :style="{ backgroundColor: item.color }" :title="`${item.name} (${item.color})`">
                  <span class="text-[8px] font-mono font-medium text-slate-200 leading-none">{{ item.name }}</span>
                </button>
              </div>
            </div>

            <div>
              <div class="flex items-center justify-between text-[10px] text-gray-500 dark:text-slate-400">
                <span>🎨 经典工业辅助色</span>
              </div>
              <div class="grid grid-cols-8 gap-1.5 mt-1">
                <button
                  v-for="c in ['#ffffff', '#f5f5f5', '#e0f2fe', '#dcfce7', '#fef9c3', '#111827', '#1e3a8a', '#073a26']"
                  :key="c" @click="updateBackground({ color: c })" :style="{ backgroundColor: c }"
                  class="h-5 rounded border border-[#d9d9d9] dark:border-slate-700 cursor-pointer hover:ring-2 hover:ring-[#1890ff] transition-all hover:scale-110"
                  :class="pageBackground.color === c ? 'ring-2 ring-[#1890ff]' : ''" :title="c" />
              </div>
            </div>
          </div>
        </template>

        <!-- 渐变 -->
        <template v-else-if="pageBackground.type === 'gradient'">
          <!-- 5 套主题渐变快速选择 -->
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">主题渐变快速选择</label>
            <div class="grid grid-cols-1 gap-1.5 mt-1">
              <button v-for="t in THEME_CANVAS_PRESETS" :key="t.id"
                @click="updateBackground({ gradientStart: t.gradient.start, gradientEnd: t.gradient.end, gradientAngle: t.gradient.angle })"
                class="flex items-center justify-between px-2 py-1.5 rounded-lg border text-xs cursor-pointer transition-all hover:scale-[1.01] active:scale-[0.99]"
                :class="(pageBackground.gradientStart === t.gradient.start && pageBackground.gradientEnd === t.gradient.end) ? 'ring-2 ring-[#1890ff] dark:ring-sky-400 border-transparent' : 'border-gray-200 dark:border-slate-700 hover:border-[#1890ff] dark:hover:border-sky-500'"
                :style="{
                  background: `linear-gradient(90deg, ${t.gradient.start}, ${t.gradient.end})`,
                  color: t.textColor,
                }">
                <span class="font-bold text-[10px] drop-shadow-xs">{{ t.name }}渐变</span>
                <span class="text-[9px] font-mono opacity-80">{{ t.gradient.start }} ➔ {{ t.gradient.end }}</span>
              </button>
            </div>
          </div>

          <div class="border-t border-dashed border-gray-200 dark:border-slate-800 my-1" />

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">起始色 (Start)</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="pageBackground.gradientStart || '#e0f2fe'"
                @input="updateBackground({ gradientStart: ($event.target as HTMLInputElement).value })"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="pageBackground.gradientStart || '#e0f2fe'"
                @input="updateBackground({ gradientStart: ($event.target as HTMLInputElement).value })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">终止色 (End)</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="pageBackground.gradientEnd || '#1e3a8a'"
                @input="updateBackground({ gradientEnd: ($event.target as HTMLInputElement).value })"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="pageBackground.gradientEnd || '#1e3a8a'"
                @input="updateBackground({ gradientEnd: ($event.target as HTMLInputElement).value })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">
              渐变角度 ({{ pageBackground.gradientAngle ?? 180 }}°)
            </label>
            <input type="range" min="0" max="360" step="5" :value="pageBackground.gradientAngle ?? 180"
              @input="updateBackground({ gradientAngle: parseInt(($event.target as HTMLInputElement).value) || 0 })"
              class="w-full mt-1 accent-[#1890ff]" />
          </div>
          <!-- 实时预览 -->
          <div class="h-8 rounded border border-[#d9d9d9] dark:border-slate-700 shadow-inner" :style="{
            backgroundImage: `linear-gradient(${pageBackground.gradientAngle ?? 180}deg, ${pageBackground.gradientStart || '#e0f2fe'}, ${pageBackground.gradientEnd || '#1e3a8a'})`
          }" />
        </template>

        <!-- 图片 -->
        <template v-else>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">图片 URL</label>
            <div class="flex items-center gap-1.5 mt-0.5">
              <input type="text" :value="pageBackground.imageUrl ?? ''"
                @input="updateBackground({ imageUrl: ($event.target as HTMLInputElement).value })"
                class="flex-1 min-w-0 bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 text-[#262626] dark:text-white text-xs focus:outline-none"
                placeholder="https://example.com/bg.png 或 /api/HmiImage/..." />
              <button type="button" @click="showBgImagePicker = true"
                class="shrink-0 px-2 py-1.5 rounded border border-[#1890ff] text-[#1890ff] dark:text-sky-400 dark:border-sky-500 hover:bg-[#e6f7ff] dark:hover:bg-sky-950/40 text-[10px] whitespace-nowrap transition-colors cursor-pointer">
                从图库选择
              </button>
            </div>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1 leading-snug">
              可从图库选择/上传，或填写可访问的外部图片地址。
            </p>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">填充方式</label>
            <select :value="pageBackground.imageFit || 'fill'"
              @change="updateBackground({ imageFit: ($event.target as HTMLSelectElement).value as PageBackground['imageFit'] })"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
              <option value="fill">拉伸铺满（可能变形）</option>
              <option value="contain">等比完整显示（可能留白）</option>
              <option value="cover">等比铺满裁切（可能裁边）</option>
              <option value="tile">平铺（按原始尺寸重复）</option>
            </select>
          </div>

          <!-- 背景选图库（内嵌实例，选择后写入 imageUrl） -->
          <ImageLibraryDialog v-model="showBgImagePicker" @select="onPickBackgroundImage" />
        </template>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800 my-4" />

      <!-- 自适应屏幕设置 -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Expand class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          自适应屏幕
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">运行端适配模式</label>
          <select :value="backgroundPage.adaptMode ?? ''"
            @change="onAdaptModeChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="">默认（等比缩小，不放大）</option>
            <option value="FitScaleUp">等比缩放（允许放大填满）</option>
            <option value="Stretch">拉伸填满（非等比，可能变形）</option>
          </select>
          <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1.5 leading-relaxed">
            仅作用于运行端全屏查看：画面按所选模式缩放适配视口。<br />
            设计端画布不受影响，可随时用工具栏缩放查看。
          </p>
        </div>
      </section>
    </div>
  </div>

  <div v-else
    class="h-full flex flex-col bg-white dark:bg-slate-900 text-[#262626] dark:text-slate-100 overflow-y-auto transition-colors">
    <!-- Title -->
    <div
      class="p-4 border-b border-[#f0f0f0] dark:border-slate-800 bg-[#fafafa] dark:bg-slate-950 flex items-center justify-between">
      <div class="flex items-center gap-2">
        <Layout class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
        <h3 class="text-xs font-bold text-[#141414] dark:text-slate-100 uppercase tracking-wider">
          属性配置
        </h3>
      </div>
      <button @click="emit('collapse')"
        class="p-1 rounded text-slate-400 hover:text-[#1890ff] dark:hover:text-sky-400 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
        title="收起属性面板">
        <ChevronRight class="w-4 h-4" />
      </button>
    </div>

    <div class="p-4 space-y-4 text-left">
      <!-- Core Layout section -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Sliders class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          布局属性
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">元件标识 (ID)</label>
            <input type="text" disabled :value="selectedComponent.id"
              class="w-full bg-[#fafafa] dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 mt-0.5 text-gray-400 dark:text-slate-500 font-mono text-[10px] cursor-not-allowed" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">元件名称</label>
            <input type="text" :value="selectedComponent.name"
              @input="updateComponentField('name', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">X 轴坐标 (px)</label>
            <input type="number" :value="selectedComponent.x"
              @input="updateComponentField('x', parseInt(($event.target as HTMLInputElement).value) || 0)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white font-mono focus:outline-none" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">Y 轴坐标 (px)</label>
            <input type="number" :value="selectedComponent.y"
              @input="updateComponentField('y', parseInt(($event.target as HTMLInputElement).value) || 0)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white font-mono focus:outline-none" />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">宽度 (Width)</label>
            <input type="number" :value="selectedComponent.width"
              @input="updateComponentField('width', parseInt(($event.target as HTMLInputElement).value) || 20)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white font-mono focus:outline-none" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400 font-mono">高度 (Height)</label>
            <input type="number" :value="selectedComponent.height"
              @input="updateComponentField('height', parseInt(($event.target as HTMLInputElement).value) || 20)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white font-mono focus:outline-none" />
          </div>
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">图层顺序 (Z-Index)</label>
          <input type="number" :value="selectedComponent.zIndex ?? 1"
            @input="updateComponentField('zIndex', parseInt(($event.target as HTMLInputElement).value) || 1)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none" />
        </div>

        <!-- PS-style Layer Assignment & Component State -->
        <div v-if="layers && layers.length > 0">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">所属图层 (PS 图层管理)</label>
          <select :value="selectedComponent.layerId || (layers[0]?.id ?? 'layer-default')"
            @change="updateComponentField('layerId', ($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option v-for="l in layers" :key="l.id" :value="l.id">
              {{ l.name }} {{ l.locked ? '🔒' : '' }} {{ l.visible === false ? '👁️(隐)' : '' }}
            </option>
          </select>
        </div>

        <div class="grid grid-cols-2 gap-2 text-xs pt-1">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">元件可见性</label>
            <button type="button"
              @click="updateComponentField('visible', selectedComponent.visible === false ? true : false)"
              class="w-full flex items-center justify-center gap-1.5 py-1.5 px-2 rounded border text-xs font-medium transition-colors mt-0.5 cursor-pointer"
              :class="selectedComponent.visible !== false
                ? 'bg-slate-100 dark:bg-slate-800 border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200'
                : 'bg-amber-50 dark:bg-amber-950/40 border-amber-300 dark:border-amber-800 text-amber-700 dark:text-amber-400'">
              <Eye v-if="selectedComponent.visible !== false" class="w-3.5 h-3.5 text-[#1890ff]" />
              <EyeOff v-else class="w-3.5 h-3.5 text-amber-500" />
              <span>{{ selectedComponent.visible !== false ? '显示 (正常)' : '隐藏 (画布隐藏)' }}</span>
            </button>
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">元件锁定状态</label>
            <button type="button"
              @click="updateComponentField('locked', selectedComponent.locked === true ? false : true)"
              class="w-full flex items-center justify-center gap-1.5 py-1.5 px-2 rounded border text-xs font-medium transition-colors mt-0.5 cursor-pointer"
              :class="selectedComponent.locked === true
                ? 'bg-rose-50 dark:bg-rose-950/40 border-rose-300 dark:border-rose-800 text-rose-700 dark:text-rose-400'
                : 'bg-slate-100 dark:bg-slate-800 border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200'">
              <Lock v-if="selectedComponent.locked === true" class="w-3.5 h-3.5 text-rose-500" />
              <Unlock v-else class="w-3.5 h-3.5 text-slate-400" />
              <span>{{ selectedComponent.locked === true ? '已锁定 (禁止拖拽)' : '未锁定 (可编辑)' }}</span>
            </button>
          </div>
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800 my-4" />

      <!-- PLC Register binding selector -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Tag class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          数据绑定
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">
            {{ selectedComponent?.type === 'multi-var-dashboard' ? '默认绑定设备（预设设备）' : '绑定设备' }}
          </label>
          <select :value="selectedComponent?.bindDeviceId ?? ''"
            @change="onBindDeviceChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="">-- 未绑定设备（禁止裸 key）--</option>
            <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }} ({{ d.key }})</option>
          </select>
          <p v-if="selectedComponent?.bindDeviceId == null && selectedComponent?.type !== 'multi-var-dashboard'"
            class="text-[10px] text-amber-600 dark:text-amber-400 mt-1 leading-relaxed">
            未绑定设备：运行态将无法定位变量值，且禁止裸 key 写入。请先选择设备。
          </p>
        </div>
        <div v-if="selectedComponent?.type !== 'multi-var-dashboard'">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">绑定变量</label>
          <select
            :value="(selectedComponent?.bindDeviceId != null ? selectedComponent?.bindVariableKey : selectedComponent?.bindField) ?? ''"
            @change="onBindVariableChange(($event.target as HTMLSelectElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs">
            <option value="">-- 无绑定 --</option>
            <option v-for="v in bindingVariableOptions" :key="v.key" :value="v.key">{{ v.key }}</option>
          </select>
        </div>
        <div v-else
          class="text-[10px] text-sky-600 dark:text-sky-400 bg-sky-50 dark:bg-sky-950/40 border border-sky-200 dark:border-sky-900 rounded p-2 flex items-start gap-1.5">
          <Sparkles class="w-3.5 h-3.5 shrink-0 mt-0.5" />
          <span>多变量看板支持绑定任意多个变量点位，请在下方「多变量监控列表」中管理和配置具体点位。</span>
        </div>
      </section>

      <div class="border-t border-[#f0f0f0] dark:border-slate-800 my-4" />

      <!-- Widget specifics customization -->
      <section class="space-y-3">
        <div class="flex items-center gap-1.5 text-xs font-semibold text-gray-700 dark:text-slate-300">
          <Hash class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400" />
          组件属性
        </div>

        <div>
          <label class="text-[10px] text-gray-500 dark:text-slate-400">标签</label>
          <textarea rows="2" :value="selectedComponent.label"
            @input="updateComponentField('label', ($event.target as HTMLTextAreaElement).value)"
            class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1.5 mt-0.5 text-[#262626] dark:text-white focus:outline-none text-xs" />
        </div>

        <!-- showLabel — 外框浮签标签默认隐藏，勾选后显示（与 widgetRegistry.baseProps 真相源一致；排除本就无浮签的内部标签型组件） -->
        <div class="flex items-center gap-2"
          v-if="!['text', 'led', 'gauge-level', 'gauge-dial', 'digital-val'].includes(selectedComponent.type)">
          <input type="checkbox" id="showLabelDef" :checked="componentProps.showLabel || false"
            @change="updateProp('showLabel', ($event.target as HTMLInputElement).checked)"
            class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
          <label htmlFor="showLabelDef" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
            显示外框标签名称
          </label>
        </div>

        <!-- var-display 外观显隐：边框/背景/内部标签独立开关，支持边框颜色、粗细、圆角及样式 -->
        <div v-if="selectedComponent.type === 'var-display'"
          class="space-y-3 text-xs border border-gray-100 dark:border-slate-800 p-2.5 rounded-lg bg-gray-50/50 dark:bg-slate-950/60">
          <div class="flex items-center justify-between">
            <p class="font-bold text-emerald-600 dark:text-emerald-400 text-[10px] uppercase tracking-wider">
              外观与边框设置
            </p>
            <span class="text-[9px] text-gray-400 dark:text-slate-500 font-mono">var-display</span>
          </div>

          <!-- 1. 显示边框开关 -->
          <div class="space-y-2">
            <div class="flex items-center justify-between">
              <label class="flex items-center gap-2 select-none cursor-pointer">
                <input type="checkbox" id="vdispShowBorder" :checked="componentProps.showBorder === true"
                  @change="updateProp('showBorder', ($event.target as HTMLInputElement).checked)"
                  class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
                <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示边框 (Show Border)</span>
              </label>
              <span class="text-[9px] font-mono"
                :class="componentProps.showBorder ? 'text-[#1890ff] font-semibold' : 'text-gray-400'">
                {{ componentProps.showBorder ? '已显示' : '已隐藏' }}
              </span>
            </div>

            <!-- 当开启显示边框时，展开边框详细样式配置 -->
            <div v-if="componentProps.showBorder === true"
              class="space-y-2 pl-5 pt-1 border-l-2 border-[#1890ff]/30 dark:border-sky-500/30">
              <!-- 边框颜色 -->
              <div>
                <label class="text-[10px] text-gray-500 dark:text-slate-400">边框颜色</label>
                <div class="flex items-center gap-1.5 mt-0.5">
                  <input type="color" :value="componentProps.borderColor || componentProps.strokeColor || '#cbd5e1'"
                    @input="updateProp('borderColor', ($event.target as HTMLInputElement).value); updateProp('strokeColor', ($event.target as HTMLInputElement).value)"
                    class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
                  <input type="text" :value="componentProps.borderColor || componentProps.strokeColor || '#cbd5e1'"
                    @input="updateProp('borderColor', ($event.target as HTMLInputElement).value); updateProp('strokeColor', ($event.target as HTMLInputElement).value)"
                    class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1.5 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
                </div>
                <!-- 快捷边框色 -->
                <div class="flex items-center gap-1 mt-1.5">
                  <button
                    v-for="bc in ['#cbd5e1', '#94a3b8', '#475569', '#1890ff', '#38bdf8', '#10b981', '#f59e0b', '#ef4444', '#1e293b']"
                    :key="bc" type="button" @click="updateProp('borderColor', bc); updateProp('strokeColor', bc)"
                    class="w-4 h-4 rounded-full border border-black/20 dark:border-white/20 cursor-pointer transition-transform hover:scale-125"
                    :style="{ backgroundColor: bc }" :title="bc" />
                </div>
              </div>

              <!-- 边框粗细与样式 -->
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">边框粗细</label>
                  <select :value="componentProps.borderWidth ?? 1.5"
                    @change="updateProp('borderWidth', numInput(($event.target as HTMLSelectElement).value, 1.5))"
                    class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
                    <option :value="1">1 px (细)</option>
                    <option :value="1.5">1.5 px (标准)</option>
                    <option :value="2">2 px (中等)</option>
                    <option :value="3">3 px (粗)</option>
                    <option :value="4">4 px (加粗)</option>
                  </select>
                </div>
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">边框线条</label>
                  <select :value="componentProps.borderStyle || 'solid'"
                    @change="updateProp('borderStyle', ($event.target as HTMLSelectElement).value)"
                    class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
                    <option value="solid">实线 (Solid)</option>
                    <option value="dashed">虚线 (Dashed)</option>
                    <option value="dotted">点线 (Dotted)</option>
                  </select>
                </div>
              </div>

              <!-- 圆角大小 -->
              <div>
                <label class="text-[10px] text-gray-500 dark:text-slate-400">圆角弧度</label>
                <div class="flex items-center gap-2 mt-0.5">
                  <input type="range" min="0" max="24" step="2" :value="componentProps.borderRadius ?? 8"
                    @input="updateProp('borderRadius', numInput(($event.target as HTMLInputElement).value, 8))"
                    class="flex-1 accent-[#1890ff]" />
                  <span class="text-[10px] font-mono text-gray-600 dark:text-slate-300 w-8 text-right">{{
                    componentProps.borderRadius ?? 8 }}px</span>
                </div>
              </div>
            </div>
          </div>

          <!-- 2. 显示背景开关 -->
          <div class="space-y-1.5 pt-1.5 border-t border-gray-200/60 dark:border-slate-800">
            <div class="flex items-center justify-between">
              <label class="flex items-center gap-2 select-none cursor-pointer">
                <input type="checkbox" id="vdispShowBg" :checked="componentProps.showBackground === true"
                  @change="updateProp('showBackground', ($event.target as HTMLInputElement).checked)"
                  class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
                <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示背景底色</span>
              </label>
              <span class="text-[9px] font-mono"
                :class="componentProps.showBackground ? 'text-[#1890ff] font-semibold' : 'text-gray-400'">
                {{ componentProps.showBackground ? '已显示' : '透明' }}
              </span>
            </div>

            <!-- 背景颜色配置 -->
            <div v-if="componentProps.showBackground === true"
              class="pl-5 pt-1 space-y-1.5 border-l-2 border-[#1890ff]/30 dark:border-sky-500/30">
              <label class="text-[10px] text-gray-500 dark:text-slate-400">底色</label>
              <div class="flex items-center gap-1.5">
                <input type="color" :value="componentProps.bgColor || '#ffffff'"
                  @input="updateProp('bgColor', ($event.target as HTMLInputElement).value)"
                  class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
                <input type="text" :value="componentProps.bgColor || '#ffffff'"
                  @input="updateProp('bgColor', ($event.target as HTMLInputElement).value)"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1.5 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
              </div>
              <div class="flex items-center gap-1 mt-1">
                <button v-for="bgc in ['#ffffff', '#f8fafc', '#f1f5f9', '#0f172a', '#061426', '#111c2e', '#1e293b']"
                  :key="bgc" type="button" @click="updateProp('bgColor', bgc)"
                  class="w-4 h-4 rounded-full border border-black/20 dark:border-white/20 cursor-pointer transition-transform hover:scale-125"
                  :style="{ backgroundColor: bgc }" :title="bgc" />
              </div>
            </div>
          </div>

          <!-- 3. 显示内部标签开关 -->
          <div class="pt-1.5 border-t border-gray-200/60 dark:border-slate-800">
            <label class="flex items-center gap-2 select-none cursor-pointer">
              <input type="checkbox" id="vdispShowInnerLabel" :checked="componentProps.showInnerLabel === true"
                @change="updateProp('showInnerLabel', ($event.target as HTMLInputElement).checked)"
                class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
              <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示内部变量标签</span>
            </label>
          </div>

          <!-- 4. 报警边框变色联动开关 -->
          <div class="pt-1.5 border-t border-gray-200/60 dark:border-slate-800">
            <label class="flex items-center gap-2 select-none cursor-pointer">
              <input type="checkbox" id="vdispEnableAlarmBorder" :checked="componentProps.enableAlarmBorder !== false"
                @change="updateProp('enableAlarmBorder', ($event.target as HTMLInputElement).checked)"
                class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
              <span class="text-xs text-gray-700 dark:text-slate-300">超限时报警变色 (红/黄报警边框)</span>
            </label>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-0.5 leading-snug pl-5">
              仅在配置了有效报警阈值且变量超限时生效；正常状态下严格遵从「显示边框」设置。
            </p>
          </div>
        </div>

        <!-- States color picks -->
        <div class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">运行激活光效</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="componentProps.activeColor || '#1890ff'"
                @input="updateProp('activeColor', ($event.target as HTMLInputElement).value)"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.activeColor || '#1890ff'"
                @input="updateProp('activeColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">空闲正常底色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="componentProps.inactiveColor || '#8c8c8c'"
                @input="updateProp('inactiveColor', ($event.target as HTMLInputElement).value)"
                class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.inactiveColor || '#8c8c8c'"
                @input="updateProp('inactiveColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>
        </div>

        <!-- 阶段5-6：状态文本解耦——阀/数显/开关等有状态控件的开/关文案可配置，去除硬编码英/中文 -->
        <div v-if="['valve', 'digital-val', 'switch', 'led', 'var-display'].includes(selectedComponent.type)"
          class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">开启状态文本</label>
            <input type="text" :value="componentProps.onText ?? '开启'"
              @input="updateProp('onText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="默认: 开启" />
          </div>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">关闭状态文本</label>
            <input type="text" :value="componentProps.offText ?? '关闭'"
              @input="updateProp('offText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="默认: 关闭" />
          </div>
        </div>

        <!-- Medium Fluid Filler style -->
        <div v-if="['tank', 'boiler', 'conveyor'].includes(selectedComponent.type)">
          <label class="text-[10px] text-gray-500 dark:text-slate-400">填充介质颜色 (Medium)</label>
          <div class="flex items-center gap-1.5 mt-1">
            <input type="color" :value="componentProps.fillColor || '#1890ff'"
              @input="updateProp('fillColor', ($event.target as HTMLInputElement).value)"
              class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
            <input type="text" :value="componentProps.fillColor || '#1890ff'"
              @input="updateProp('fillColor', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 focus:outline-none text-gray-600 dark:text-slate-300" />
          </div>
        </div>

        <!-- 量程设置：量程上限/下限/单位（百分比类与仪表类归一化基准） -->
        <div
          v-if="['gauge-dial', 'gauge-level', 'digital-val', 'var-display', 'tank', 'boiler', 'trend-chart', 'pump', 'motor'].includes(selectedComponent.type)"
          class="space-y-2">
          <div class="grid grid-cols-3 gap-2 text-xs">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">量程下限 (Min)</label>
              <input type="number" :value="componentProps.minValue ?? typeDefaults.minValue ?? 0"
                @input="updateProp('minValue', numInput(($event.target as HTMLInputElement).value, 0))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">量程上限 (Max)</label>
              <input type="number" :value="componentProps.maxValue ?? typeDefaults.maxValue ?? 100"
                @input="updateProp('maxValue', numInput(($event.target as HTMLInputElement).value, typeDefaults.maxValue ?? 100))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">单位 (Unit)</label>
              <input type="text" :value="componentProps.unit ?? ''"
                @input="updateProp('unit', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none"
                placeholder="e.g. L/s, MPa, ℃" />
            </div>
          </div>
        </div>

        <!-- 高/低限报警阈值：覆盖所有消费 isHighAlert/alertColor 的器件 -->
        <div
          v-if="['gauge-dial', 'gauge-level', 'digital-val', 'var-display', 'boiler', 'pump', 'motor', 'trend-chart', 'led'].includes(selectedComponent.type)"
          class="grid grid-cols-2 gap-2 text-xs">
          <div>
            <label class="text-[10px] text-red-500 dark:text-red-400">红色高限报警值</label>
            <input type="number" :value="componentProps.thresholdMax ?? typeDefaults.thresholdMax ?? ''"
              @input="updateProp('thresholdMax', ($event.target as HTMLInputElement).value === '' ? null : numInput(($event.target as HTMLInputElement).value, 90))"
              placeholder="默认不设"
              class="w-full bg-white dark:bg-slate-950 border border-red-300 dark:border-red-800 rounded px-2.5 py-1 text-red-600 dark:text-red-400 focus:outline-none focus:border-red-500" />
          </div>
          <div>
            <label class="text-[10px] text-amber-600 dark:text-amber-400">黄色低限预警值</label>
            <input type="number" :value="componentProps.thresholdMin ?? typeDefaults.thresholdMin ?? ''"
              @input="updateProp('thresholdMin', ($event.target as HTMLInputElement).value === '' ? null : numInput(($event.target as HTMLInputElement).value, 10))"
              placeholder="默认不设"
              class="w-full bg-white dark:bg-slate-950 border border-amber-300 dark:border-amber-800 rounded px-2.5 py-1 text-amber-700 dark:text-amber-300 focus:outline-none focus:border-amber-500" />
          </div>
        </div>

        <!-- 趋势图多序列绑定（trend-chart）：支持多变量 + 逐线颜色/粗细自定义 -->
        <div v-if="selectedComponent.type === 'trend-chart'"
          class="space-y-3 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-1.5">
              <p class="font-bold text-sky-600 dark:text-sky-400 text-[11px] uppercase tracking-wider">趋势序列 (多变量)</p>
              <span
                class="text-[9px] font-mono px-1.5 py-0.5 rounded-full bg-sky-100 dark:bg-sky-900/60 text-sky-700 dark:text-sky-300">
                {{ trendSeries.length }} 条
              </span>
            </div>
            <button type="button" @click="importAllVariablesForTrend()"
              class="flex items-center gap-1 px-2 py-1 rounded bg-sky-600 hover:bg-sky-500 text-white text-[10px] font-medium transition-all shadow-sm cursor-pointer"
              title="一键将当前设备的所有变量导入为趋势序列">
              <Sparkles class="w-3 h-3" />
              <span>导入设备全部变量</span>
            </button>
          </div>

          <!-- 空列表提示 -->
          <div v-if="trendSeries.length === 0"
            class="p-4 rounded border border-dashed border-slate-300 dark:border-slate-700 bg-white/60 dark:bg-slate-900/60 text-center space-y-2">
            <p class="text-xs text-slate-500 dark:text-slate-400">暂未添加任何趋势序列</p>
            <div class="flex items-center justify-center gap-2">
              <button type="button" @click="addTrendSeries"
                class="px-3 py-1 rounded bg-[#1890ff] text-white text-xs font-medium hover:bg-[#40a9ff] transition-colors cursor-pointer">
                + 添加序列
              </button>
              <button type="button" @click="importAllVariablesForTrend()"
                class="px-3 py-1 rounded bg-sky-600 text-white text-xs font-medium hover:bg-sky-500 transition-colors cursor-pointer">
                一键导入全部
              </button>
            </div>
            <p v-if="selectedComponent.bindDeviceId == null || !selectedComponent.bindVariableKey"
              class="text-[9px] text-amber-600 dark:text-amber-400">提示：也可先在上方「变量绑定」区绑定一个变量，打开此组件时会自动升级为第 1 条序列。</p>
          </div>

          <!-- 序列条目列表 -->
          <div v-else class="space-y-2.5 max-h-[480px] overflow-y-auto pr-0.5">
            <div v-for="(s, idx) in trendSeries" :key="s.id || idx"
              class="p-2.5 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shadow-sm space-y-2 transition-all hover:border-sky-300 dark:hover:border-sky-800">
              <!-- 头部：序号 + 移动排序 + 删除 -->
              <div class="flex items-center justify-between pb-1 border-b border-slate-100 dark:border-slate-800">
                <div class="flex items-center gap-1.5">
                  <span class="w-4 h-4 rounded-full flex items-center justify-center text-[10px] font-mono font-bold text-white"
                    :style="{ background: s.color }">{{ idx + 1 }}</span>
                  <span class="font-bold text-slate-800 dark:text-slate-200 text-xs truncate max-w-[120px]">
                    {{ s.label || s.variableKey }}
                  </span>
                </div>
                <div class="flex items-center gap-1">
                  <button type="button" @click="moveTrendSeries(idx, -1)" :disabled="idx === 0"
                    class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
                    title="上移">
                    <ChevronUp class="w-3.5 h-3.5" />
                  </button>
                  <button type="button" @click="moveTrendSeries(idx, 1)" :disabled="idx === trendSeries.length - 1"
                    class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
                    title="下移">
                    <ChevronDown class="w-3.5 h-3.5" />
                  </button>
                  <button type="button" @click="removeTrendSeries(idx)"
                    class="p-1 rounded text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-950/40 transition-colors cursor-pointer" title="删除此序列">
                    <Trash2 class="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>

              <!-- 变量绑定设置 (设备 + 变量) -->
              <div class="grid grid-cols-2 gap-1.5">
                <div>
                  <label class="text-[9px] text-slate-400">所属设备</label>
                  <select :value="s.deviceId ?? selectedComponent.bindDeviceId ?? devices[0]?.id ?? ''"
                    @change="updateTrendSeries(idx, { deviceId: ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null })"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
                    <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
                  </select>
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">绑定变量</label>
                  <select :value="s.variableKey"
                    @change="updateTrendSeries(idx, { variableKey: ($event.target as HTMLSelectElement).value, label: s.label || ($event.target as HTMLSelectElement).value })"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
                    <option v-for="v in getItemVariableOptions(s.deviceId)" :key="v.key" :value="v.key">
                      {{ v.name }} ({{ v.key }})
                    </option>
                  </select>
                </div>
              </div>

              <!-- 图例名称 + 单位 -->
              <div class="grid grid-cols-2 gap-1.5">
                <div>
                  <label class="text-[9px] text-slate-400">图例名称</label>
                  <input type="text" :value="s.label ?? ''"
                    @input="updateTrendSeries(idx, { label: ($event.target as HTMLInputElement).value })"
                    placeholder="自动显示变量名"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">单位 (Unit)</label>
                  <input type="text" :value="s.unit ?? ''"
                    @input="updateTrendSeries(idx, { unit: ($event.target as HTMLInputElement).value })"
                    placeholder="例如 ℃, MPa"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                </div>
              </div>

              <!-- 颜色 + 线宽 -->
              <div class="grid grid-cols-2 gap-1.5 items-end">
                <div>
                  <label class="text-[9px] text-slate-400">线条颜色</label>
                  <div class="flex items-center gap-1.5">
                    <input type="color" :value="s.color"
                      @input="updateTrendSeries(idx, { color: ($event.target as HTMLInputElement).value })"
                      class="w-8 h-7 p-0 border border-slate-200 dark:border-slate-700 rounded bg-transparent cursor-pointer" />
                    <span class="text-[10px] font-mono text-slate-500">{{ s.color }}</span>
                  </div>
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">线条粗细 (px): {{ s.lineWidth }}</label>
                  <input type="range" min="1" max="8" step="0.5" :value="s.lineWidth"
                    @input="updateTrendSeries(idx, { lineWidth: Number(($event.target as HTMLInputElement).value) })"
                    class="w-full accent-[#1890ff] dark:accent-sky-500" />
                </div>
              </div>

              <!-- 序列级量程/阈值（可选；空则按全局自适应） -->
              <div class="grid grid-cols-2 gap-1.5 pt-1 border-t border-dashed border-slate-100 dark:border-slate-800">
                <div>
                  <label class="text-[9px] text-slate-400">量程下限 (空=全局)</label>
                  <input type="number" :value="s.minValue ?? ''"
                    @input="updateTrendSeries(idx, { minValue: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
                    placeholder="全局自适应"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-slate-800 dark:text-slate-200 focus:outline-none" />
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">量程上限 (空=全局)</label>
                  <input type="number" :value="s.maxValue ?? ''"
                    @input="updateTrendSeries(idx, { maxValue: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
                    placeholder="全局自适应"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-slate-800 dark:text-slate-200 focus:outline-none" />
                </div>
                <div>
                  <label class="text-[9px] text-amber-600 dark:text-amber-400">低限预警 (≤ 变黄)</label>
                  <input type="number" :value="s.thresholdMin ?? ''"
                    @input="updateTrendSeries(idx, { thresholdMin: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
                    placeholder="默认不设"
                    class="w-full bg-amber-50/40 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-900 rounded px-1.5 py-0.5 text-[10px] text-amber-800 dark:text-amber-300 focus:outline-none" />
                </div>
                <div>
                  <label class="text-[9px] text-rose-600 dark:text-rose-400">高限报警 (≥ 变红)</label>
                  <input type="number" :value="s.thresholdMax ?? ''"
                    @input="updateTrendSeries(idx, { thresholdMax: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
                    placeholder="默认不设"
                    class="w-full bg-rose-50/40 dark:bg-rose-950/20 border border-rose-200 dark:border-rose-900 rounded px-1.5 py-0.5 text-[10px] text-rose-800 dark:text-rose-300 focus:outline-none" />
                </div>
              </div>
            </div>
          </div>

          <!-- 底部新增按钮 -->
          <button type="button" @click="addTrendSeries"
            class="w-full py-1.5 rounded border border-dashed border-sky-400 dark:border-sky-700 bg-white/70 dark:bg-slate-900/70 hover:bg-sky-50 dark:hover:bg-sky-950/40 text-sky-700 dark:text-sky-300 text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors cursor-pointer">
            <Plus class="w-3.5 h-3.5" />
            <span>添加趋势序列</span>
          </button>
        </div>

        <!-- var-display 数据变量显示专属配置：小数位 / 可设定 / 写入范围 / 二次确认 -->
        <div v-if="selectedComponent.type === 'var-display'"
          class="space-y-2.5 text-xs border border-gray-100 dark:border-slate-800 p-2.5 rounded bg-gray-50/50 dark:bg-slate-950/60">
          <p class="font-bold text-[#1890ff] dark:text-sky-400 text-[10px] uppercase tracking-wider">变量显示配置</p>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">小数位数 (0~4)</label>
            <select :value="componentProps.decimals ?? 2"
              @change="updateProp('decimals', numInput(($event.target as HTMLSelectElement).value, 2))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option v-for="n in [0, 1, 2, 3, 4]" :key="n" :value="n">{{ n }} 位</option>
            </select>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-0.5 leading-snug">
              显示与设值弹窗输入同步约束；写入前按位数四舍五入。
            </p>
          </div>

          <div class="flex items-center gap-2">
            <input type="checkbox" id="settableDef" :checked="componentProps.settable === true"
              @change="updateProp('settable', ($event.target as HTMLInputElement).checked)"
              class="accent-[#1890ff] dark:accent-sky-500" />
            <label for="settableDef" class="text-[11px] text-gray-700 dark:text-slate-300 cursor-pointer">
              可设定（运行态点击弹出数字键盘写值）
            </label>
          </div>
          <p v-if="componentProps.settable !== true"
            class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug -mt-1.5">
            未开启时组件仅作显示；写值仍需绑定设备/变量且有 Operator/Admin 权限。
          </p>

          <template v-if="componentProps.settable === true">
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-[10px] text-gray-500 dark:text-slate-400">写入下限（空=不限）</label>
                <input type="number" :value="componentProps.writeMin ?? ''"
                  @input="updateProp('writeMin', ($event.target as HTMLInputElement).value === '' ? null : numInput(($event.target as HTMLInputElement).value, 0))"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500"
                  placeholder="不限制" />
              </div>
              <div>
                <label class="text-[10px] text-gray-500 dark:text-slate-400">写入上限（空=不限）</label>
                <input type="number" :value="componentProps.writeMax ?? ''"
                  @input="updateProp('writeMax', ($event.target as HTMLInputElement).value === '' ? null : numInput(($event.target as HTMLInputElement).value, 0))"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500"
                  placeholder="不限制" />
              </div>
            </div>
            <div class="flex items-center gap-2">
              <input type="checkbox" id="confirmReqDef" :checked="componentProps.confirmRequired === true"
                @change="updateProp('confirmRequired', ($event.target as HTMLInputElement).checked)"
                class="accent-[#1890ff] dark:accent-sky-500" />
              <label for="confirmReqDef" class="text-[11px] text-gray-700 dark:text-slate-300 cursor-pointer">
                写入前二次确认（高危变量防误写）
              </label>
            </div>
          </template>
        </div>

        <!-- INDUSTRIAL BUTTON SPECIFIC CONTROLS -->
        <div v-if="selectedComponent.type === 'button'"
          class="space-y-2 text-xs border border-gray-100 dark:border-slate-800 p-2 rounded bg-gray-50/50 dark:bg-slate-950/60">
          <p class="font-bold text-[#1890ff] dark:text-sky-400 text-[10px] uppercase tracking-wider mb-1">按钮功能配置</p>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">操作类型 (Action Mode)</label>
            <select :value="componentProps.buttonMode || 'toggle'"
              @change="updateProp('buttonMode', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="toggle">主锁/自锁 (Toggle - 单击取反)</option>
              <option value="momentary">按1送0 / 点动 (Momentary - 按下1松开0)</option>
              <option value="set-bit">置位 (SetBit - 写入1)</option>
              <option value="reset-bit">复位 (ResetBit - 写入0)</option>
              <option value="set-value">恒定设值 (SetValue - 写入固定值)</option>
              <option value="navigate">画面跳转 (Navigate - 跳转到同端其它画面)</option>
            </select>
          </div>

          <div v-if="componentProps.buttonMode === 'set-value'">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">点击写入的数值</label>
            <input type="number" :value="componentProps.clickValue ?? 1"
              @input="updateProp('clickValue', numInput(($event.target as HTMLInputElement).value, 1))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs" />
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">按钮文本说明 (Static Label)</label>
            <input type="text" :value="componentProps.buttonText ?? ''"
              @input="updateProp('buttonText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="默认取本级Label" />
          </div>

          <!-- 阶段3：导航模式 → 选择同端目标画面 -->
          <div v-if="componentProps.buttonMode === 'navigate'">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">跳转目标画面（仅同端）</label>
            <select :value="componentProps.targetPageId ?? ''"
              @change="updateProp('targetPageId', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="">-- 请选择目标画面 --</option>
              <option v-for="opt in navTargetOptions" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
            </select>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1 leading-snug">
              运行时点击该按钮将跳转到所选画面；跨端跳转不允许，下拉仅列出「{{ currentPlatform === 'Mobile' ? '移动端' : '桌面端' }}」画面。
            </p>
          </div>
        </div>

        <!-- NAV-MENU SPECIFIC CONTROLS（导航菜单专属配置：3~5 项，图标/文字/跳转目标） -->
        <div v-if="selectedComponent.type === 'nav-menu'"
          class="space-y-2.5 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
          <p class="font-bold text-sky-600 dark:text-sky-400 text-[10px] uppercase tracking-wider">导航菜单配置</p>
          <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
            端型：{{ componentProps.menuDevice === 'mobile' ? '移动端·底部标签栏' : '桌面端·顶部导航条' }}；
            菜单项 {{ menuItems.length }}/{{ MENU_ITEM_MAX }}，跳转目标仅限「{{ currentPlatform === 'Mobile' ? '移动端' : '桌面端'
            }}」画面（不含当前页）。
          </p>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">风格主题 (Style Preset)</label>
            <select :value="componentProps.menuStyle || 'navy-midnight'"
              @change="updateProp('menuStyle', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <optgroup label="☀️ 浅色大方系列">
                <option value="pure-white">极简亮白 (Pure Crisp White · 浅色)</option>
                <option value="titanium-light">工业钛灰 (Titanium Light · 浅色)</option>
              </optgroup>
              <optgroup label="🌙 深色稳健系列">
                <option value="slate-dark">经典石板深灰 (Classic Slate · 深色)</option>
                <option value="navy-midnight">深海商务暗蓝 (Navy Midnight · 深色)</option>
              </optgroup>
              <optgroup label="🌿 轻量通透系列">
                <option value="translucent-frost">悬浮通透胶囊 (Adaptive Frost · 通透)</option>
              </optgroup>
              <optgroup label="⚙️ 经典特色预设">
                <option value="eco-green">生态翡翠绿 (Eco Green)</option>
                <option value="carbon-orange">机能碳纤橙 (Carbon Orange)</option>
                <option value="tech-blue">科技蓝 (Tech Blue)</option>
              </optgroup>
            </select>
          </div>

          <!-- 菜单项列表 -->
          <div v-for="(item, idx) in menuItems" :key="idx"
            class="border border-gray-200 dark:border-slate-700 rounded p-2 space-y-1.5 bg-white/70 dark:bg-slate-950/50">
            <div class="flex items-center justify-between">
              <span class="text-[10px] font-bold text-gray-500 dark:text-slate-400">菜单项 {{ idx + 1 }}</span>
              <div class="flex items-center gap-0.5">
                <button type="button" @click="moveMenuItem(idx, -1)" :disabled="idx === 0"
                  class="p-1 rounded hover:bg-gray-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed text-gray-500 dark:text-slate-400"
                  title="上移">
                  <ChevronUp class="w-3.5 h-3.5" />
                </button>
                <button type="button" @click="moveMenuItem(idx, 1)" :disabled="idx === menuItems.length - 1"
                  class="p-1 rounded hover:bg-gray-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed text-gray-500 dark:text-slate-400"
                  title="下移">
                  <ChevronDown class="w-3.5 h-3.5" />
                </button>
                <button type="button" @click="removeMenuItem(idx)" :disabled="menuItems.length <= MENU_ITEM_MIN"
                  class="p-1 rounded hover:bg-red-50 dark:hover:bg-red-950/50 disabled:opacity-30 disabled:cursor-not-allowed text-red-500"
                  :title="menuItems.length <= MENU_ITEM_MIN ? `最少保留 ${MENU_ITEM_MIN} 项` : '删除该项'">
                  <Trash2 class="w-3.5 h-3.5" />
                </button>
              </div>
            </div>

            <!-- 图标选择：按钮展开内置图标网格 -->
            <div class="flex items-center gap-2">
              <button type="button" @click="openIconPickerIndex = openIconPickerIndex === idx ? -1 : idx"
                class="shrink-0 w-8 h-8 rounded border border-gray-200 dark:border-slate-700 flex items-center justify-center hover:border-[#1890ff] dark:hover:border-sky-500 transition-colors"
                :class="openIconPickerIndex === idx ? 'border-[#1890ff]! dark:border-sky-500!' : ''"
                :title="`选择图标（当前: ${item.icon}）`">
                <component :is="getMenuIcon(item.icon)" class="w-4 h-4 text-[#1890ff] dark:text-sky-400" />
              </button>
              <div class="flex-1 min-w-0">
                <label class="text-[10px] text-gray-500 dark:text-slate-400">显示文字</label>
                <input type="text" :value="item.text" maxlength="10"
                  @input="updateMenuItem(idx, { text: ($event.target as HTMLInputElement).value })"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500" />
              </div>
            </div>

            <!-- 内置图标网格（仅当前展开项显示） -->
            <div v-if="openIconPickerIndex === idx"
              class="grid grid-cols-9 gap-1 p-1.5 rounded border border-gray-100 dark:border-slate-800 bg-gray-50/60 dark:bg-slate-900/60">
              <button v-for="opt in MENU_ICON_OPTIONS" :key="opt.name" type="button"
                @click="updateMenuItem(idx, { icon: opt.name }); openIconPickerIndex = -1"
                class="aspect-square rounded flex items-center justify-center transition-all" :class="item.icon === opt.name
                  ? 'bg-[#1890ff]/15 ring-1 ring-[#1890ff] dark:ring-sky-500'
                  : 'hover:bg-gray-200/70 dark:hover:bg-slate-800'" :title="opt.label">
                <component :is="opt.icon" class="w-3.5 h-3.5"
                  :class="item.icon === opt.name ? 'text-[#1890ff] dark:text-sky-400' : 'text-gray-500 dark:text-slate-400'" />
              </button>
            </div>

            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">跳转目标画面（仅同端）</label>
              <select :value="item.targetPageId ?? ''"
                @change="updateMenuItem(idx, { targetPageId: (($event.target as HTMLSelectElement).value || null) })"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 text-xs text-[#262626] dark:text-white">
                <option value="">-- 请选择目标画面 --</option>
                <option v-for="opt in navTargetOptions" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
              </select>
            </div>
          </div>

          <!-- 新增菜单项（上限 5） -->
          <button type="button" @click="addMenuItem" :disabled="menuItems.length >= MENU_ITEM_MAX"
            class="w-full py-1.5 rounded border border-dashed border-[#1890ff]/60 dark:border-sky-500/60 text-[#1890ff] dark:text-sky-400 hover:bg-[#1890ff]/5 dark:hover:bg-sky-500/10 disabled:opacity-40 disabled:cursor-not-allowed flex items-center justify-center gap-1 transition-colors">
            <Plus class="w-3.5 h-3.5" />
            添加菜单项（3~5 项）
          </button>

          <!-- 主题微调：强调色 / 字号 -->
          <div class="grid grid-cols-2 gap-2">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">强调色</label>
              <input type="color" :value="componentProps.menuAccentColor ?? '#38bdf8'"
                @input="updateProp('menuAccentColor', ($event.target as HTMLInputElement).value)"
                class="w-full h-7 bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded cursor-pointer" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">文字字号 (px)</label>
              <input type="number" min="10" max="22" :value="componentProps.menuFontSize ?? 14"
                @input="updateProp('menuFontSize', numInput(($event.target as HTMLInputElement).value, 14))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500" />
            </div>
          </div>
        </div>

        <!-- IMAGE WIDGET SPECIFIC CONTROLS（图片图元专属配置） -->
        <div v-if="selectedComponent.type === 'image'"
          class="space-y-2 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
          <p class="font-bold text-sky-600 dark:text-sky-400 text-[10px] uppercase tracking-wider">图片配置</p>

          <!-- 预览 -->
          <div
            class="h-28 rounded border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-950 flex items-center justify-center overflow-hidden">
            <img v-if="(componentProps.imageUrl || '').trim()" :src="componentProps.imageUrl" alt=""
              class="max-h-full max-w-full object-contain" draggable="false" />
            <span v-else class="text-[10px] text-slate-400 dark:text-slate-500">未设置图片</span>
          </div>

          <button type="button" @click="showImagePicker = true"
            class="w-full py-1.5 rounded bg-[#1890ff] hover:bg-[#40a9ff] text-white text-xs font-medium transition-colors cursor-pointer">
            更换图片（从图库选择/上传）
          </button>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">填充方式</label>
            <select :value="componentProps.imageFit || 'fill'"
              @change="updateProp('imageFit', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="fill">拉伸填满（可能变形）</option>
              <option value="contain">等比完整显示（可能留白）</option>
              <option value="cover">等比铺满裁切（可能裁边）</option>
              <option value="tile">平铺（按原始尺寸重复）</option>
            </select>
          </div>

          <!-- 图元换图库（组件内嵌实例，与图元添加/背景选图互不影响） -->
          <ImageLibraryDialog v-model="showImagePicker" @select="onPickComponentImage" />
        </div>

        <!-- 大屏标题背景图元专属配置（title-header）三套风格 -->
        <div v-if="selectedComponent.type === 'title-header'"
          class="space-y-3 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
          <div class="flex items-center justify-between">
            <p class="font-bold text-sky-600 dark:text-sky-400 text-[11px] uppercase tracking-wider">大屏标题背景设置
            </p>
            <span
              class="text-[9px] font-mono bg-sky-100 dark:bg-sky-900/60 text-sky-700 dark:text-sky-300 px-1.5 py-0.5 rounded">Title
              Header</span>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">风格主题 (Style Preset)</label>
            <select :value="componentProps.headerStyle || 'navy-midnight'"
              @change="updateProp('headerStyle', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <optgroup label="☀️ 浅色大方系列">
                <option value="pure-white">极简亮白 (Pure Crisp White · 浅色)</option>
                <option value="titanium-light">工业钛灰 (Titanium Light · 浅色)</option>
              </optgroup>
              <optgroup label="🌙 深色稳健系列">
                <option value="slate-dark">经典石板深灰 (Classic Slate · 深色)</option>
                <option value="navy-midnight">深海商务暗蓝 (Navy Midnight · 深色)</option>
              </optgroup>
              <optgroup label="🌿 轻量通透系列">
                <option value="translucent-frost">悬浮通透胶囊 (Adaptive Frost · 通透)</option>
              </optgroup>
              <optgroup label="⚙️ 经典特色预设">
                <option value="eco-green">生态翡翠绿 (Eco Green)</option>
                <option value="carbon-orange">机能碳纤橙 (Carbon Orange)</option>
                <option value="tech-blue">科技蓝 (Tech Blue)</option>
              </optgroup>
            </select>
          </div>

          <div class="grid grid-cols-2 gap-2">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">主标题 (Title)</label>
              <input type="text" :value="componentProps.headerTitle ?? ''"
                @input="updateProp('headerTitle', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
                placeholder="大屏主标题" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">副标题 (Subtitle)</label>
              <input type="text" :value="componentProps.headerSubtitle ?? ''"
                @input="updateProp('headerSubtitle', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
                placeholder="英文/副标题（可留空）" />
            </div>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">角标 / Logo 文字</label>
            <input type="text" :value="componentProps.headerLogoText ?? ''"
              @input="updateProp('headerLogoText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="SCADA">
          </div>

          <div class="grid grid-cols-2 gap-2 pt-1">
            <label class="flex items-center gap-2 text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              <input type="checkbox" id="headerClock" :checked="componentProps.headerShowClock !== false"
                @change="updateProp('headerShowClock', ($event.target as HTMLInputElement).checked)"
                class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] dark:text-sky-500 focus:ring-0" />
              显示动态时钟
            </label>
            <label class="flex items-center gap-2 text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              <input type="checkbox" id="headerStatus" :checked="componentProps.headerShowStatus !== false"
                @change="updateProp('headerShowStatus', ($event.target as HTMLInputElement).checked)"
                class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] dark:text-sky-500 focus:ring-0" />
              显示运行状态
            </label>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">状态文案 (Status Text)</label>
            <input type="text" :value="componentProps.headerStatusText ?? ''"
              @input="updateProp('headerStatusText', ($event.target as HTMLInputElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs"
              placeholder="系统运行正常" />
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">发光 / 主辅高亮色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="componentProps.headerGlowColor || '#38bdf8'"
                @input="updateProp('headerGlowColor', ($event.target as HTMLInputElement).value)"
                class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.headerGlowColor || '#38bdf8'"
                @input="updateProp('headerGlowColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-2 pt-1 border-t border-sky-100 dark:border-sky-900/40">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">字体大小 (px)</label>
              <input type="number" :value="componentProps.fontSize ?? 22"
                @input="updateProp('fontSize', numInput(($event.target as HTMLInputElement).value, 22))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white mt-0.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 text-xs" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">字重</label>
              <select :value="componentProps.bold ? 'bold' : 'normal'"
                @change="updateProp('bold', ($event.target as HTMLSelectElement).value === 'bold')"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white mt-0.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 text-xs">
                <option value="bold">加粗 (Bold)</option>
                <option value="normal">常规 (Normal)</option>
              </select>
            </div>
          </div>
        </div>

        <!-- INDUSTRIAL ROUNDED BUTTON SPECIFIC CONTROLS (圆角按钮专属配置) -->
        <div v-if="selectedComponent.type === 'rounded-btn'"
          class="space-y-3 text-xs border border-emerald-200/80 dark:border-emerald-900/60 p-3 rounded-lg bg-emerald-50/40 dark:bg-emerald-950/20">
          <div class="flex items-center justify-between">
            <p class="font-bold text-emerald-600 dark:text-emerald-400 text-[11px] uppercase tracking-wider">圆角按钮与多状态配置
            </p>
            <span
              class="text-[9px] font-mono bg-emerald-100 dark:bg-emerald-900/60 text-emerald-700 dark:text-emerald-300 px-1.5 py-0.5 rounded">Rounded
              Button</span>
          </div>

          <!-- 预设风格一键应用（启动/停止/复位/点动/急停） -->
          <div>
            <label class="text-[10px] font-semibold text-gray-700 dark:text-slate-300">预设按钮风格 (Preset Styles)</label>
            <div class="grid grid-cols-5 gap-1 mt-1">
              <button type="button" @click="applyRoundedBtnPreset('start')"
                class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#16a34a] hover:brightness-110 active:scale-95 transition-all cursor-pointer">
                启动
              </button>
              <button type="button" @click="applyRoundedBtnPreset('stop')"
                class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#dc2626] hover:brightness-110 active:scale-95 transition-all cursor-pointer">
                停止
              </button>
              <button type="button" @click="applyRoundedBtnPreset('reset')"
                class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#2563eb] hover:brightness-110 active:scale-95 transition-all cursor-pointer">
                复位
              </button>
              <button type="button" @click="applyRoundedBtnPreset('jog')"
                class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#ea580c] hover:brightness-110 active:scale-95 transition-all cursor-pointer">
                点动
              </button>
              <button type="button" @click="applyRoundedBtnPreset('estop')"
                class="rounded px-1 py-1.5 text-[10px] font-bold text-white bg-[#991b1b] border border-red-400 hover:brightness-110 active:scale-95 transition-all cursor-pointer">
                急停
              </button>
            </div>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 mt-1 leading-snug">
              一键套用工业标准配色与控制模式（启动=置位/停止=复位清零/复位=脉冲/点动=按1送0/急停=置位+粗边框），应用后可继续微调。
            </p>
          </div>

          <!-- 控制模式选择 -->
          <div>
            <label class="text-[10px] font-semibold text-gray-700 dark:text-slate-300">控制动作模式 (Action Mode)</label>
            <select :value="componentProps.buttonMode || 'toggle'"
              @change="updateProp('buttonMode', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 focus:outline-none focus:border-emerald-500 mt-1 text-xs text-[#262626] dark:text-white font-medium">
              <option value="toggle">取反 (Toggle - 0变1，1变0)</option>
              <option value="set-bit">置位 (SetBit - 强制写入1 / True)</option>
              <option value="reset-bit">复位 (ResetBit - 强制写入0 / False)</option>
              <option value="momentary">按1送0 (Momentary - 按下写1松开写0)</option>
              <option value="set-value">恒定设值 (SetValue - 写入指定数值)</option>
              <option value="navigate">画面跳转 (Navigate - 跳转同端画面)</option>
              <option value="run-script">执行脚本 (RunScript - 触发服务端系统脚本)</option>
            </select>
          </div>

          <!-- 模式角标显隐开关 -->
          <div class="flex items-center gap-2">
            <input type="checkbox" id="showModeBadgeDef" :checked="componentProps.showModeBadge !== false"
              @change="updateProp('showModeBadge', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] dark:border-slate-700 text-emerald-600 focus:ring-0" />
            <label for="showModeBadgeDef" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              显示模式角标文字（[取反]/[置位1]/[脚本] 等）
            </label>
          </div>

          <div v-if="componentProps.buttonMode === 'set-value'">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">设值写入数值</label>
            <input type="number" :value="componentProps.clickValue ?? 1"
              @input="updateProp('clickValue', numInput(($event.target as HTMLInputElement).value, 1))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-emerald-500 mt-0.5 text-xs" />
          </div>

          <div v-if="componentProps.buttonMode === 'navigate'">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">跳转目标画面（仅同端）</label>
            <select :value="componentProps.targetPageId ?? ''"
              @change="updateProp('targetPageId', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-emerald-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="">-- 请选择目标画面 --</option>
              <option v-for="opt in navTargetOptions" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
            </select>
          </div>

          <div v-if="componentProps.buttonMode === 'run-script'" class="space-y-1.5">
            <label class="text-[10px] text-gray-500 dark:text-slate-400">触发执行的系统脚本</label>
            <!-- 脚本列表（管理员自动加载；无权限/为空时回退手填 ID） -->
            <select v-if="systemScripts.length > 0" :value="componentProps.targetScriptId ?? ''"
              @change="updateProp('targetScriptId', ($event.target as HTMLSelectElement).value === '' ? null : Number(($event.target as HTMLSelectElement).value))"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-emerald-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="">-- 请选择脚本 --</option>
              <option v-for="s in systemScripts" :key="s.id" :value="s.id">#{{ s.id }} {{ s.name }}{{ s.active ? '' :
                '（已停用）' }}</option>
            </select>
            <template v-else>
              <input type="number" :value="componentProps.targetScriptId ?? ''"
                @input="updateProp('targetScriptId', numInput(($event.target as HTMLInputElement).value, 0) || null)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-emerald-500"
                placeholder="输入系统脚本 ID" />
              <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
                脚本列表仅管理员可加载；可直接填写脚本 ID。运行态点击按钮将触发服务端沙箱执行。
              </p>
            </template>
          </div>

          <!-- 操作变量绑定（写入目标）：可与上方「数据绑定」（背景/显示变量）分离 -->
          <div v-if="!['navigate', 'run-script'].includes(componentProps.buttonMode)"
            class="space-y-2 pt-2 border-t border-emerald-100 dark:border-emerald-900/40">
            <p class="font-bold text-gray-700 dark:text-slate-300 text-[10px]">操作变量绑定（写入目标）</p>
            <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
              不配置时与「数据绑定」一致；配置后点击写入此变量，按钮背景状态仍由数据绑定变量驱动。
            </p>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">操作设备</label>
              <select :value="componentProps.opDeviceId ?? ''"
                @change="onOpDeviceChange(($event.target as HTMLSelectElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 mt-0.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-emerald-500">
                <option value="">-- 跟随数据绑定设备 --</option>
                <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }} ({{ d.key }})</option>
              </select>
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">操作变量</label>
              <select :value="componentProps.opVariableKey ?? ''"
                @change="onOpVariableChange(($event.target as HTMLSelectElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1.5 mt-0.5 text-xs text-[#262626] dark:text-white focus:outline-none focus:border-emerald-500">
                <option value="">-- 跟随数据绑定变量 --</option>
                <option v-for="v in opBindingVariableOptions" :key="v.key" :value="v.key">{{ v.key }}</option>
              </select>
            </div>
          </div>

          <!-- 圆角与边框精细调节 -->
          <div class="grid grid-cols-2 gap-2 text-xs pt-1 border-t border-emerald-100 dark:border-emerald-900/40">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">圆角弧度 (Radius: {{ componentProps.borderRadius
                ?? 10 }}px)</label>
              <input type="range" min="0" max="40" step="1" :value="componentProps.borderRadius ?? 10"
                @input="updateProp('borderRadius', parseInt(($event.target as HTMLInputElement).value) || 0)"
                class="w-full mt-1 accent-emerald-600 dark:accent-emerald-400" />
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">边框粗细 (Border: {{ componentProps.borderWidth
                ?? 1 }}px)</label>
              <input type="range" min="0" max="6" step="1" :value="componentProps.borderWidth ?? 1"
                @input="updateProp('borderWidth', parseInt(($event.target as HTMLInputElement).value) || 0)"
                class="w-full mt-1 accent-emerald-600 dark:accent-emerald-400" />
            </div>
          </div>

          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">边框轮廓颜色</label>
            <div class="flex items-center gap-1.5 mt-1">
              <input type="color" :value="componentProps.strokeColor || '#38bdf8'"
                @input="updateProp('strokeColor', ($event.target as HTMLInputElement).value)"
                class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
              <input type="text" :value="componentProps.strokeColor || '#38bdf8'"
                @input="updateProp('strokeColor', ($event.target as HTMLInputElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
            </div>
          </div>

          <!-- 双态/多态配置：状态0 (OFF/停止) 与 状态1 (ON/运行) -->
          <div class="space-y-2 pt-2 border-t border-emerald-100 dark:border-emerald-900/40">
            <p class="font-bold text-gray-700 dark:text-slate-300 text-[10px]">基础状态样式定义 (状态 0 / 1)</p>

            <!-- 状态0 (值=0/false) -->
            <div
              class="bg-white dark:bg-slate-900 p-2 rounded border border-slate-200 dark:border-slate-800 space-y-1.5">
              <div class="flex items-center justify-between">
                <span class="text-[10px] font-bold text-slate-500 dark:text-slate-400">● 状态 0 (关/停止/0)</span>
              </div>
              <div class="grid grid-cols-3 gap-1.5">
                <div class="col-span-1">
                  <label class="text-[9px] text-slate-400">显示文本</label>
                  <input type="text" :value="componentProps.state0Text ?? 'OFF 停止'"
                    @input="updateProp('state0Text', ($event.target as HTMLInputElement).value)"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[11px] text-slate-800 dark:text-white" />
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">背景色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.state0BgColor || '#1e293b'"
                      @input="updateProp('state0BgColor', ($event.target as HTMLInputElement).value)"
                      class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.state0BgColor || '#1e293b'"
                      @input="updateProp('state0BgColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">文字颜色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.state0TextColor || '#94a3b8'"
                      @input="updateProp('state0TextColor', ($event.target as HTMLInputElement).value)"
                      class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.state0TextColor || '#94a3b8'"
                      @input="updateProp('state0TextColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
              </div>
            </div>

            <!-- 状态1 (值=1/true) -->
            <div
              class="bg-white dark:bg-slate-900 p-2 rounded border border-slate-200 dark:border-slate-800 space-y-1.5">
              <div class="flex items-center justify-between">
                <span class="text-[10px] font-bold text-emerald-600 dark:text-emerald-400">● 状态 1 (开/运行/1)</span>
              </div>
              <div class="grid grid-cols-3 gap-1.5">
                <div class="col-span-1">
                  <label class="text-[9px] text-slate-400">显示文本</label>
                  <input type="text" :value="componentProps.state1Text ?? 'ON 运行'"
                    @input="updateProp('state1Text', ($event.target as HTMLInputElement).value)"
                    class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[11px] text-slate-800 dark:text-white" />
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">背景色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.state1BgColor || '#0284c7'"
                      @input="updateProp('state1BgColor', ($event.target as HTMLInputElement).value)"
                      class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.state1BgColor || '#0284c7'"
                      @input="updateProp('state1BgColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
                <div>
                  <label class="text-[9px] text-slate-400">文字颜色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.state1TextColor || '#ffffff'"
                      @input="updateProp('state1TextColor', ($event.target as HTMLInputElement).value)"
                      class="w-5 h-5 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.state1TextColor || '#ffffff'"
                      @input="updateProp('state1TextColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
              </div>
            </div>

            <!-- 高级多状态自定义 (Custom States) -->
            <div class="space-y-1 pt-1">
              <label class="text-[10px] text-gray-500 dark:text-slate-400 flex justify-between">
                <span>高级自定义多状态规则 (值:文本:背景色:字色)</span>
              </label>
              <textarea rows="2" :value="componentProps.customStates ?? ''"
                @input="updateProp('customStates', ($event.target as HTMLTextAreaElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 mt-0.5 focus:outline-none focus:border-emerald-500 text-[10px] font-mono text-gray-700 dark:text-slate-300 leading-relaxed"
                placeholder="0:停止:#334155:#94a3b8;1:运行:#0284c7:#ffffff;2:报警:#dc2626:#ffffff" />
              <p class="text-[9px] text-gray-400 dark:text-slate-500 leading-snug">
                支持任意数值状态映射，例如 <code
                  class="bg-slate-100 dark:bg-slate-800 px-1 rounded">0:停止:#1e293b:#94a3b8;1:运行:#10b981:#ffffff;2:过载:#f59e0b:#ffffff;3:紧急故障:#ef4444:#ffffff</code>
              </p>
            </div>
          </div>
        </div>

        <!-- TIME CLOCK WIDGET FORMATS -->
        <div v-if="selectedComponent.type === 'sys-time'"
          class="space-y-2 text-xs border border-gray-100 dark:border-slate-800 p-2 rounded bg-gray-50/50 dark:bg-slate-950/60">
          <p class="font-bold text-emerald-600 dark:text-emerald-400 text-[10px] uppercase tracking-wider mb-1">系统时间显示设置
          </p>
          <div>
            <label class="text-[10px] text-gray-500 dark:text-slate-400">排版格式 (DateTime Format)</label>
            <select :value="componentProps.timeFormat || 'HH:mm:ss'"
              @change="updateProp('timeFormat', ($event.target as HTMLSelectElement).value)"
              class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none focus:border-[#1890ff] dark:focus:border-sky-500 mt-0.5 text-xs text-[#262626] dark:text-white">
              <option value="HH:mm:ss">时分秒 (HH:mm:ss)</option>
              <option value="YYYY-MM-DD HH:mm:ss">年月日 时分秒</option>
              <option value="YYYY-MM-DD">仅显示日期 (YYYY-MM-DD)</option>
            </select>
          </div>
        </div>

        <!-- 23. REAL-TIME MULTI-VARIABLE DASHBOARD CONTROLS (实时多变量监控看板专属配置) -->
        <div v-if="selectedComponent.type === 'multi-var-dashboard'" class="space-y-4">
          <!-- 模块一：看板排版与外框设置 -->
          <div
            class="space-y-3 text-xs border border-sky-200/80 dark:border-sky-900/60 p-3 rounded-lg bg-sky-50/40 dark:bg-sky-950/20">
            <div class="flex items-center justify-between">
              <p
                class="font-bold text-sky-600 dark:text-sky-400 text-[11px] uppercase tracking-wider flex items-center gap-1.5">
                <LayoutDashboard class="w-3.5 h-3.5" />
                看板布局与边框设置
              </p>
              <span
                class="text-[9px] font-mono bg-sky-100 dark:bg-sky-900/60 text-sky-700 dark:text-sky-300 px-1.5 py-0.5 rounded">Dashboard</span>
            </div>

            <!-- 看板标题设置 -->
            <div class="space-y-2 pb-2 border-b border-sky-100 dark:border-sky-900/40">
              <div class="flex items-center justify-between">
                <label class="flex items-center gap-2 select-none cursor-pointer">
                  <input type="checkbox" id="dashShowTitle" :checked="componentProps.showDashboardTitle !== false"
                    @change="updateProp('showDashboardTitle', ($event.target as HTMLInputElement).checked)"
                    class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
                  <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示看板标题栏</span>
                </label>
              </div>

              <div v-if="componentProps.showDashboardTitle !== false" class="space-y-1.5">
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">标题名称</label>
                  <input type="text" :value="componentProps.dashboardTitle ?? '实时参数监控看板'"
                    @input="updateProp('dashboardTitle', ($event.target as HTMLInputElement).value)"
                    class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2.5 py-1 text-gray-800 dark:text-white focus:outline-none focus:border-[#1890ff] text-xs mt-0.5"
                    placeholder="看板标题" />
                </div>
              </div>
            </div>

            <!-- 排版布局模式 (Layout Mode) -->
            <div>
              <label class="text-[10px] font-semibold text-gray-700 dark:text-slate-300">排版模式 (Layout)</label>
              <div class="grid grid-cols-3 gap-1.5 mt-1">
                <button type="button" @click="updateProp('dashboardLayout', 'grid')"
                  class="flex flex-col items-center gap-1 p-2 rounded border text-center transition-all cursor-pointer"
                  :class="(!componentProps.dashboardLayout || componentProps.dashboardLayout === 'grid')
                    ? 'bg-[#1890ff]/10 border-[#1890ff] text-[#1890ff] font-bold dark:bg-sky-950/60 dark:border-sky-500'
                    : 'bg-white dark:bg-slate-900 border-gray-200 dark:border-slate-800 text-gray-600 dark:text-slate-400'">
                  <Grid class="w-4 h-4" />
                  <span class="text-[10px]">卡片网格</span>
                </button>
                <button type="button" @click="updateProp('dashboardLayout', 'table')"
                  class="flex flex-col items-center gap-1 p-2 rounded border text-center transition-all cursor-pointer"
                  :class="componentProps.dashboardLayout === 'table'
                    ? 'bg-[#1890ff]/10 border-[#1890ff] text-[#1890ff] font-bold dark:bg-sky-950/60 dark:border-sky-500'
                    : 'bg-white dark:bg-slate-900 border-gray-200 dark:border-slate-800 text-gray-600 dark:text-slate-400'">
                  <Table class="w-4 h-4" />
                  <span class="text-[10px]">列表表格</span>
                </button>
                <button type="button" @click="updateProp('dashboardLayout', 'compact')"
                  class="flex flex-col items-center gap-1 p-2 rounded border text-center transition-all cursor-pointer"
                  :class="componentProps.dashboardLayout === 'compact'
                    ? 'bg-[#1890ff]/10 border-[#1890ff] text-[#1890ff] font-bold dark:bg-sky-950/60 dark:border-sky-500'
                    : 'bg-white dark:bg-slate-900 border-gray-200 dark:border-slate-800 text-gray-600 dark:text-slate-400'">
                  <Columns class="w-4 h-4" />
                  <span class="text-[10px]">紧凑微标</span>
                </button>
              </div>
            </div>

            <!-- 列数与间距设置 (仅卡片网格模式) -->
            <div v-if="!componentProps.dashboardLayout || componentProps.dashboardLayout === 'grid'"
              class="grid grid-cols-2 gap-2">
              <div>
                <label class="text-[10px] text-gray-500 dark:text-slate-400">排版列数 (Columns)</label>
                <select :value="componentProps.dashboardColumns ?? 2"
                  @change="updateProp('dashboardColumns', numInput(($event.target as HTMLSelectElement).value, 2))"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
                  <option :value="1">1 列 (单列垂直)</option>
                  <option :value="2">2 列 (双列卡片)</option>
                  <option :value="3">3 列 (三列排版)</option>
                  <option :value="4">4 列 (四列密集)</option>
                  <option :value="6">6 列 (六列大屏)</option>
                  <option :value="0">Auto (自适应流式)</option>
                </select>
              </div>
              <div>
                <label class="text-[10px] text-gray-500 dark:text-slate-400">间距大小 (Gap)</label>
                <select :value="componentProps.dashboardGap ?? 8"
                  @change="updateProp('dashboardGap', numInput(($event.target as HTMLSelectElement).value, 8))"
                  class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
                  <option :value="4">4 px (紧凑)</option>
                  <option :value="8">8 px (标准)</option>
                  <option :value="12">12 px (舒适)</option>
                  <option :value="16">16 px (宽松)</option>
                  <option :value="20">20 px (超宽)</option>
                </select>
              </div>
            </div>

            <!-- 表格模式斑马纹 -->
            <div v-if="componentProps.dashboardLayout === 'table'" class="flex items-center gap-2">
              <input type="checkbox" id="dashZebra" :checked="componentProps.dashboardZebra === true"
                @change="updateProp('dashboardZebra', ($event.target as HTMLInputElement).checked)"
                class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
              <label for="dashZebra" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
                启用表格行隔行斑马纹
              </label>
            </div>

            <!-- 看板外框边框 (Border) -->
            <div class="space-y-2 pt-2 border-t border-sky-100 dark:border-sky-900/40">
              <div class="flex items-center justify-between">
                <label class="flex items-center gap-2 select-none cursor-pointer">
                  <input type="checkbox" id="dashShowBorder" :checked="componentProps.showBorder !== false"
                    @change="updateProp('showBorder', ($event.target as HTMLInputElement).checked)"
                    class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
                  <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示看板外框边框</span>
                </label>
              </div>

              <div v-if="componentProps.showBorder !== false"
                class="space-y-2 pl-4 border-l-2 border-sky-200 dark:border-sky-800">
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">外框边框颜色</label>
                  <div class="flex items-center gap-1.5 mt-0.5">
                    <input type="color" :value="componentProps.borderColor || '#cbd5e1'"
                      @input="updateProp('borderColor', ($event.target as HTMLInputElement).value)"
                      class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
                    <input type="text" :value="componentProps.borderColor || '#cbd5e1'"
                      @input="updateProp('borderColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1.5 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
                  </div>
                  <div class="flex items-center gap-1 mt-1.5">
                    <button
                      v-for="bc in ['#cbd5e1', '#94a3b8', '#475569', '#1890ff', '#38bdf8', '#10b981', '#f59e0b', '#ef4444', '#1e293b']"
                      :key="bc" type="button" @click="updateProp('borderColor', bc)"
                      class="w-4 h-4 rounded-full border border-black/20 dark:border-white/20 cursor-pointer transition-transform hover:scale-125"
                      :style="{ backgroundColor: bc }" :title="bc" />
                  </div>
                </div>

                <div class="grid grid-cols-2 gap-2">
                  <div>
                    <label class="text-[10px] text-gray-500 dark:text-slate-400">边框粗细</label>
                    <select :value="componentProps.borderWidth ?? 1.5"
                      @change="updateProp('borderWidth', numInput(($event.target as HTMLSelectElement).value, 1.5))"
                      class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
                      <option :value="1">1 px (细)</option>
                      <option :value="1.5">1.5 px (标准)</option>
                      <option :value="2">2 px (中等)</option>
                      <option :value="3">3 px (粗)</option>
                      <option :value="4">4 px (加粗)</option>
                    </select>
                  </div>
                  <div>
                    <label class="text-[10px] text-gray-500 dark:text-slate-400">边框线条</label>
                    <select :value="componentProps.borderStyle || 'solid'"
                      @change="updateProp('borderStyle', ($event.target as HTMLSelectElement).value)"
                      class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs text-[#262626] dark:text-white mt-0.5">
                      <option value="solid">实线 (Solid)</option>
                      <option value="dashed">虚线 (Dashed)</option>
                      <option value="dotted">点线 (Dotted)</option>
                    </select>
                  </div>
                </div>

                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">外框圆角弧度</label>
                  <div class="flex items-center gap-2 mt-0.5">
                    <input type="range" min="0" max="24" step="2" :value="componentProps.borderRadius ?? 8"
                      @input="updateProp('borderRadius', numInput(($event.target as HTMLInputElement).value, 8))"
                      class="flex-1 accent-[#1890ff]" />
                    <span class="text-[10px] font-mono text-gray-600 dark:text-slate-300 w-8 text-right">{{
                      componentProps.borderRadius ?? 8 }}px</span>
                  </div>
                </div>
              </div>
            </div>

            <!-- 看板背景底色 -->
            <div class="space-y-2 pt-2 border-t border-sky-100 dark:border-sky-900/40">
              <div class="flex items-center justify-between">
                <label class="flex items-center gap-2 select-none cursor-pointer">
                  <input type="checkbox" id="dashShowBg" :checked="componentProps.showBackground !== false"
                    @change="updateProp('showBackground', ($event.target as HTMLInputElement).checked)"
                    class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
                  <span class="text-xs font-semibold text-gray-700 dark:text-slate-300">显示看板背景底色</span>
                </label>
              </div>

              <div v-if="componentProps.showBackground !== false"
                class="space-y-2 pl-4 border-l-2 border-sky-200 dark:border-sky-800">
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">背景颜色</label>
                  <div class="flex items-center gap-1.5 mt-0.5">
                    <input type="color" :value="componentProps.bgColor || '#ffffff'"
                      @input="updateProp('bgColor', ($event.target as HTMLInputElement).value)"
                      class="w-7 h-7 bg-transparent border-0 cursor-pointer rounded overflow-hidden" />
                    <input type="text" :value="componentProps.bgColor || '#ffffff'"
                      @input="updateProp('bgColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[10px] px-1.5 py-1 font-mono text-gray-600 dark:text-slate-300 focus:outline-none" />
                  </div>
                  <div class="flex items-center gap-1 mt-1.5">
                    <button v-for="bg in ['#ffffff', '#f8fafc', '#f1f5f9', '#e2e8f0', '#0f172a', '#1e293b', '#030712']"
                      :key="bg" type="button" @click="updateProp('bgColor', bg)"
                      class="w-4 h-4 rounded-full border border-black/20 dark:border-white/20 cursor-pointer transition-transform hover:scale-125"
                      :style="{ backgroundColor: bg }" :title="bg" />
                  </div>
                </div>
              </div>
            </div>

            <!-- 子项卡片样式设置 (字号/子项边框/底色) -->
            <div class="space-y-2 pt-2 border-t border-sky-100 dark:border-sky-900/40">
              <p class="font-bold text-gray-700 dark:text-slate-300 text-[10px]">子项与文字样式</p>

              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">数值字号 (px)</label>
                  <input type="number" min="12" max="32" :value="componentProps.dashboardValueFontSize ?? 16"
                    @input="updateProp('dashboardValueFontSize', numInput(($event.target as HTMLInputElement).value, 16))"
                    class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none text-xs mt-0.5" />
                </div>
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">标签字号 (px)</label>
                  <input type="number" min="9" max="18" :value="componentProps.dashboardLabelFontSize ?? 11"
                    @input="updateProp('dashboardLabelFontSize', numInput(($event.target as HTMLInputElement).value, 11))"
                    class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded px-2 py-1 text-gray-800 dark:text-white focus:outline-none text-xs mt-0.5" />
                </div>
              </div>

              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">子卡片底色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.dashboardItemBgColor || '#f8fafc'"
                      @input="updateProp('dashboardItemBgColor', ($event.target as HTMLInputElement).value)"
                      class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.dashboardItemBgColor || '#f8fafc'"
                      @input="updateProp('dashboardItemBgColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
                <div>
                  <label class="text-[10px] text-gray-500 dark:text-slate-400">子卡片边框色</label>
                  <div class="flex items-center gap-1 mt-0.5">
                    <input type="color" :value="componentProps.dashboardItemBorderColor || '#e2e8f0'"
                      @input="updateProp('dashboardItemBorderColor', ($event.target as HTMLInputElement).value)"
                      class="w-6 h-6 bg-transparent border-0 cursor-pointer rounded" />
                    <input type="text" :value="componentProps.dashboardItemBorderColor || '#e2e8f0'"
                      @input="updateProp('dashboardItemBorderColor', ($event.target as HTMLInputElement).value)"
                      class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 rounded text-[9px] px-1 py-0.5 font-mono" />
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 模块二：多变量监控列表管理 (dashboardItems) -->
          <div
            class="space-y-3 text-xs border border-emerald-200/80 dark:border-emerald-900/60 p-3 rounded-lg bg-emerald-50/40 dark:bg-emerald-950/20">
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-1.5">
                <p class="font-bold text-emerald-600 dark:text-emerald-400 text-[11px] uppercase tracking-wider">
                  多变量监控点位列表
                </p>
                <span
                  class="text-[9px] font-mono px-1.5 py-0.5 rounded-full bg-emerald-100 dark:bg-emerald-900/60 text-emerald-700 dark:text-emerald-300">
                  {{ dashboardItems.length }} 项
                </span>
              </div>

              <!-- 一键导入按钮 -->
              <button type="button" @click="importAllVariablesFromDevice()"
                class="flex items-center gap-1 px-2 py-1 rounded bg-emerald-600 hover:bg-emerald-500 text-white text-[10px] font-medium transition-all shadow-sm cursor-pointer"
                title="一键将当前设备的所有变量导入到看板中">
                <Sparkles class="w-3 h-3" />
                <span>导入设备全部变量</span>
              </button>
            </div>

            <!-- 空列表提示 -->
            <div v-if="dashboardItems.length === 0"
              class="p-4 rounded border border-dashed border-slate-300 dark:border-slate-700 bg-white/60 dark:bg-slate-900/60 text-center space-y-2">
              <p class="text-xs text-slate-500 dark:text-slate-400">暂未添加任何变量点位</p>
              <div class="flex items-center justify-center gap-2">
                <button type="button" @click="addDashboardItem"
                  class="px-3 py-1 rounded bg-[#1890ff] text-white text-xs font-medium hover:bg-[#40a9ff] transition-colors cursor-pointer">
                  + 添加单项变量
                </button>
                <button type="button" @click="importAllVariablesFromDevice()"
                  class="px-3 py-1 rounded bg-emerald-600 text-white text-xs font-medium hover:bg-emerald-500 transition-colors cursor-pointer">
                  一键导入全部
                </button>
              </div>
            </div>

            <!-- 变量条目列表 -->
            <div v-else class="space-y-2.5 max-h-[480px] overflow-y-auto pr-0.5">
              <div v-for="(item, idx) in dashboardItems" :key="item.id || idx"
                class="p-2.5 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shadow-sm space-y-2 transition-all hover:border-sky-300 dark:hover:border-sky-800">
                <!-- 头部：序号 + 移动排序 + 删除 -->
                <div class="flex items-center justify-between pb-1 border-b border-slate-100 dark:border-slate-800">
                  <div class="flex items-center gap-1.5">
                    <span
                      class="w-4 h-4 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 text-[10px] font-mono font-bold flex items-center justify-center">
                      {{ idx + 1 }}
                    </span>
                    <span class="font-bold text-slate-800 dark:text-slate-200 text-xs truncate max-w-[120px]">
                      {{ item.label || item.variableKey }}
                    </span>
                  </div>

                  <div class="flex items-center gap-1">
                    <button type="button" @click="moveDashboardItem(idx, -1)" :disabled="idx === 0"
                      class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
                      title="上移">
                      <ChevronUp class="w-3.5 h-3.5" />
                    </button>
                    <button type="button" @click="moveDashboardItem(idx, 1)"
                      :disabled="idx === dashboardItems.length - 1"
                      class="p-1 rounded text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
                      title="下移">
                      <ChevronDown class="w-3.5 h-3.5" />
                    </button>
                    <button type="button" @click="removeDashboardItem(idx)"
                      class="p-1 rounded text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-950/40 transition-colors cursor-pointer"
                      title="删除此变量项">
                      <Trash2 class="w-3.5 h-3.5" />
                    </button>
                  </div>
                </div>

                <!-- 变量绑定设置 (设备 + 变量) -->
                <div class="grid grid-cols-2 gap-1.5">
                  <div>
                    <label class="text-[9px] text-slate-400">所属设备</label>
                    <select :value="item.deviceId ?? selectedComponent.bindDeviceId ?? devices[0]?.id ?? ''"
                      @change="updateDashboardItem(idx, { deviceId: ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null })"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
                      <option v-for="d in devices" :key="d.id" :value="d.id">{{ d.name }}</option>
                    </select>
                  </div>
                  <div>
                    <label class="text-[9px] text-slate-400">绑定变量</label>
                    <select :value="item.variableKey" @change="updateDashboardItem(idx, {
                      variableKey: ($event.target as HTMLSelectElement).value,
                      label: item.label || ($event.target as HTMLSelectElement).value
                    })"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none focus:border-[#1890ff]">
                      <option v-for="v in getItemVariableOptions(item.deviceId)" :key="v.key" :value="v.key">
                        {{ v.name }} ({{ v.key }})
                      </option>
                    </select>
                  </div>
                </div>

                <!-- 自定义名称与单位 -->
                <div class="grid grid-cols-2 gap-1.5">
                  <div>
                    <label class="text-[9px] text-slate-400">自定义显示名称</label>
                    <input type="text" :value="item.label ?? ''"
                      @input="updateDashboardItem(idx, { label: ($event.target as HTMLInputElement).value })"
                      placeholder="自动显示变量名"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                  </div>
                  <div>
                    <label class="text-[9px] text-slate-400">单位 (Unit)</label>
                    <input type="text" :value="item.unit ?? ''"
                      @input="updateDashboardItem(idx, { unit: ($event.target as HTMLInputElement).value })"
                      placeholder="例如 ℃, MPa, A"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-white focus:outline-none focus:border-[#1890ff]" />
                  </div>
                </div>

                <!-- 小数位与指示灯开关 -->
                <div class="grid grid-cols-2 gap-1.5 pt-0.5">
                  <div>
                    <label class="text-[9px] text-slate-400">小数位数</label>
                    <select :value="item.precision ?? ''"
                      @change="updateDashboardItem(idx, { precision: ($event.target as HTMLSelectElement).value === '' ? null : Number(($event.target as HTMLSelectElement).value) })"
                      class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1 text-[11px] text-slate-800 dark:text-slate-200 focus:outline-none">
                      <option value="">自动</option>
                      <option :value="0">0 位 (整数)</option>
                      <option :value="1">1 位小数</option>
                      <option :value="2">2 位小数</option>
                      <option :value="3">3 位小数</option>
                      <option :value="4">4 位小数</option>
                    </select>
                  </div>
                  <div class="flex items-center gap-1.5 pt-4">
                    <input type="checkbox" :id="`dot-${idx}`" :checked="item.showStatusDot !== false"
                      @change="updateDashboardItem(idx, { showStatusDot: ($event.target as HTMLInputElement).checked })"
                      class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
                    <label :for="`dot-${idx}`"
                      class="text-[10px] text-slate-700 dark:text-slate-300 cursor-pointer select-none">
                      显示状态指示圆点
                    </label>
                  </div>
                </div>

                <!-- 阈值报警设置 (可选) -->
                <div
                  class="grid grid-cols-2 gap-1.5 pt-1 border-t border-dashed border-slate-100 dark:border-slate-800">
                  <div>
                    <label class="text-[9px] text-amber-600 dark:text-amber-400">低限预警值 (≤ 变黄)</label>
                    <input type="number" :value="item.thresholdMin ?? ''"
                      @input="updateDashboardItem(idx, { thresholdMin: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
                      placeholder="默认不设"
                      class="w-full bg-amber-50/40 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-900 rounded px-1.5 py-0.5 text-[10px] text-amber-800 dark:text-amber-300 focus:outline-none" />
                  </div>
                  <div>
                    <label class="text-[9px] text-rose-600 dark:text-rose-400">高限报警值 (≥ 变红)</label>
                    <input type="number" :value="item.thresholdMax ?? ''"
                      @input="updateDashboardItem(idx, { thresholdMax: ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value) })"
                      placeholder="默认不设"
                      class="w-full bg-rose-50/40 dark:bg-rose-950/20 border border-rose-200 dark:border-rose-900 rounded px-1.5 py-0.5 text-[10px] text-rose-800 dark:text-rose-300 focus:outline-none" />
                  </div>
                </div>
              </div>
            </div>

            <!-- 底部新增按钮 -->
            <button type="button" @click="addDashboardItem"
              class="w-full py-1.5 rounded border border-dashed border-emerald-400 dark:border-emerald-700 bg-white/70 dark:bg-slate-900/70 hover:bg-emerald-50 dark:hover:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors cursor-pointer">
              <Plus class="w-3.5 h-3.5" />
              <span>添加监控变量项</span>
            </button>
          </div>
        </div>

        <!-- Custom fonts controls for Text boxes -->
        <div v-if="['text', 'button', 'rounded-btn'].includes(selectedComponent.type)" class="space-y-2 text-xs">
          <div class="grid grid-cols-2 gap-2">
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">对齐方式</label>
              <select :value="componentProps.align || 'center'"
                @change="updateProp('align', ($event.target as HTMLSelectElement).value)"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1 text-gray-800 dark:text-white mt-0.5 focus:outline-none">
                <option value="left">靠左对齐</option>
                <option value="center">居中对齐</option>
                <option value="right">靠右对齐</option>
              </select>
            </div>
            <div>
              <label class="text-[10px] text-gray-500 dark:text-slate-400">字体大小 (px)</label>
              <input type="number" :value="componentProps.fontSize ?? 12"
                @input="updateProp('fontSize', numInput(($event.target as HTMLInputElement).value, 12))"
                class="w-full bg-white dark:bg-slate-950 border border-[#d9d9d9] dark:border-slate-700 hover:border-[#1890ff] focus:border-[#1890ff] dark:focus:border-sky-500 rounded px-2.5 py-1 text-[#262626] dark:text-white mt-0.5 focus:outline-none" />
            </div>
          </div>

          <div class="flex items-center gap-2 mt-2">
            <input type="checkbox" id="fontBoldDef" :checked="componentProps.bold || false"
              @change="updateProp('bold', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
            <label htmlFor="fontBoldDef" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              加粗字体 (Font Bold)
            </label>
          </div>

          <!-- 阶段：showValue — 组件内显示变量值（隐藏顶部浮签标签），复活死属性（#6） -->
          <div class="flex items-center gap-2 mt-2" v-if="selectedComponent.type !== 'text'">
            <input type="checkbox" id="showValueDef" :checked="componentProps.showValue || false"
              @change="updateProp('showValue', ($event.target as HTMLInputElement).checked)"
              class="rounded border-[#d9d9d9] dark:border-slate-700 text-[#1890ff] focus:ring-0" />
            <label htmlFor="showValueDef" class="text-xs text-gray-700 dark:text-slate-300 select-none cursor-pointer">
              组件内显示变量值（隐藏顶部浮签）
            </label>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
