<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted, watch } from 'vue';
import { HMIComponent, HmiMenuItem, HmiDashboardItem } from '../types';
import { getWidgetDef, getMenuIcon } from '../widgetRegistry';
import { devices } from '../store/deviceStore';
import { LayoutDashboard } from 'lucide-vue-next';
// 全局共享动画时钟：单实例 rAF 驱动所有动画器件，避免每组件独立 rAF（见 #13）
import { ticks, subscribeAnimation, unsubscribeAnimation } from '../utils/animationTicker';
import { isSamePageRef } from '../utils/pageId';
import { getEffectiveTrendSeries } from '../utils/trendSeries';
import type { TrendSample } from '../utils/trendHistory';
import { niceTicks, relTimeLabel, fmtTick } from '../utils/axisTicks';

const props = defineProps<{
  component: HMIComponent;
  value: number | boolean;
  isActiveMode: boolean;
  controlLocked?: boolean;
  /** 趋势图多序列数据窗口：组件 id → (序列 id → 采样点滚动缓冲 {t,v})，无则显示占位 */
  history?: Record<string, TrendSample[]>;
  /** 当前页面 id：nav-menu 运行态据此高亮“当前画面”对应的菜单项 */
  currentPageId?: string;
  /** 绑定变量质量（非 Good 时 var-display 显示 -- 而非旧值，避免误判） */
  quality?: string;
}>();

// 阶段6-2：运行模式下当前角色无写权限且本组件绑定了变量 → 标记为只读锁定控件。
// 判定口径与 CanvasPanel 写拦截（bindVariableKey || bindField）一致，避免仅 bindField 的旧组件无锁标。
const isLockedControl = computed(() =>
  !!props.controlLocked && !!(props.component.bindVariableKey || props.component.bindField)
);

const numValue = computed(() => {
  return typeof props.value === 'number' ? props.value : props.value ? 100 : 0;
});

const boolValue = computed(() => {
  return typeof props.value === 'boolean' ? props.value : props.value > 0;
});

// 量程归一化百分比（0~100）：供 tank / gauge-level / boiler 温度条使用
const normalizedPercent = computed(() => {
  const lo = minValue.value, hi = maxValue.value;
  if (hi <= lo) return numValue.value > 0 ? 100 : 0; // 量程非法防除零
  return Math.min(100, Math.max(0, ((numValue.value - lo) / (hi - lo)) * 100));
});

// 动画帧计数：使用全局共享时钟（单个 rAF 实例驱动所有器件），避免每组件独立开 rAF。
onMounted(() => {
  if (props.isActiveMode) subscribeAnimation();
});

watch(() => props.isActiveMode, (newVal) => {
  if (newVal) subscribeAnimation();
  else unsubscribeAnimation();
});

onUnmounted(() => {
  unsubscribeAnimation();
});

// Extracted prop getters —— 三级 fallback：组件 props → 注册表默认 → 硬兜底。
// 与 InspectorPanel 回显共用同一真相源（widgetRegistry.baseProps），杜绝双轨不一致。
const defDefaults = computed(() => getWidgetDef(props.component.type)?.defaultProps() ?? {});
const propOr = <T,>(key: string, hard: T): T => {
  const v = props.component.props[key as keyof HMIComponent['props']];
  return (v !== undefined && v !== null && v !== '') ? (v as T)
    : ((defDefaults.value[key] as T) ?? hard);
};

const activeColor = computed(() => propOr('activeColor', '#10b981'));
const inactiveColor = computed(() => propOr('inactiveColor', '#94a3b8'));
const strokeColor = computed(() => propOr('strokeColor', '#475569'));
const fillColor = computed(() => propOr('fillColor', '#cbd5e1'));
const minValue = computed(() => Number(propOr('minValue', 0)));
const maxValue = computed(() => Number(propOr('maxValue', 100)) || 100); // ||100 防 maxValue=0 除零
const unit = computed(() => propOr('unit', ''));
const thresholdMin = computed<number | null>(() => {
  const v = props.component.props.thresholdMin;
  if (v !== undefined && v !== null && !isNaN(Number(v))) return Number(v);
  const def = defDefaults.value.thresholdMin;
  return (def !== undefined && def !== null && !isNaN(Number(def))) ? Number(def) : null;
});
const thresholdMax = computed<number | null>(() => {
  const v = props.component.props.thresholdMax;
  if (v !== undefined && v !== null && !isNaN(Number(v))) return Number(v);
  const def = defDefaults.value.thresholdMax;
  return (def !== undefined && def !== null && !isNaN(Number(def))) ? Number(def) : null;
});
const fontSize = computed(() => Number(propOr('fontSize', 12)));
const align = computed<'left' | 'center' | 'right'>(() =>
  (propOr('align', 'center') as 'left' | 'center' | 'right') || 'center');
const bold = computed(() => propOr('bold', false));
// var-display 外观显隐：普通边框/背景/内部标签（默认隐藏，注册表为真相源）
const showBorder = computed(() => propOr('showBorder', false));
const showBackground = computed(() => propOr('showBackground', false));
const showInnerLabel = computed(() => propOr('showInnerLabel', false));

// 图片图元：填充方式 → 尺寸样式（tile 无 object-fit 对应，按原尺寸平铺由容器裁切）
const imageFitStyle = computed(() => {
  const fit = props.component.props.imageFit ?? 'fill';
  if (fit === 'tile') {
    // 原尺寸平铺：用 background-repeat 实现真·平铺，替代原先「原尺寸左上对齐退化为裁切」的分支
    return { width: 'auto', height: 'auto', maxWidth: 'none', maxHeight: 'none' };
  }
  // 显式字面量收窄，避免 string 不可赋给 CSS ObjectFit 联合类型
  const objectFit: 'fill' | 'contain' | 'cover' =
    fit === 'contain' ? 'contain' : fit === 'cover' ? 'cover' : 'fill';
  return { width: '100%', height: '100%', objectFit };
});

// #12 image 图元状态：URL 加载失败兜底 + tile 背景平铺
const imgError = ref(false);
const resetImgError = () => (imgError.value = false);
watch(() => props.component.props.imageUrl, resetImgError);
const tileStyle = computed(() => ({
  width: '100%',
  height: '100%',
  backgroundImage: `url("${props.component.props.imageUrl}")`,
  backgroundRepeat: 'repeat',
  backgroundSize: 'auto',
}));

// ===== 大屏标题背景图元（title-header）5套简约大方风格（含浅色/深色/通透） × 桌面/移动 =====
// 所有内容从 props 读取（含注册表默认兜底），文案/风格/时钟/状态均可在属性面板编辑。
const headerStyle = computed(() =>
  (propOr('headerStyle', 'navy-midnight') as string));
const headerDevice = computed<'desktop' | 'mobile'>(() =>
  (propOr('headerDevice', 'desktop') as 'desktop' | 'mobile'));
const headerTitle = computed(() => propOr('headerTitle', '工业互联网智能监控大屏'));
const headerSubtitle = computed(() => propOr('headerSubtitle', ''));
const headerLogoText = computed(() => propOr('headerLogoText', 'SCADA'));
const headerShowClock = computed(() => propOr('headerShowClock', true));
const headerShowStatus = computed(() => propOr('headerShowStatus', true));
const headerStatusText = computed(() => propOr('headerStatusText', '系统运行正常'));
const headerGlowColor = computed(() => propOr('headerGlowColor', '#38bdf8'));

// 5 套风格主题（2 浅色 + 2 深色 + 1 通透悬浮）：极简亮白 / 工业钛灰 / 经典石板深灰 / 深海商务暗蓝 / 悬浮通透胶囊
const headerTheme = computed(() => {
  const glow = headerGlowColor.value;
  const style = headerStyle.value;

  // 1. 浅色系：极简亮白 (Pure Crisp White)
  if (style === 'pure-white') {
    return {
      background: '#ffffff',
      border: '1px solid #e2e8f0',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow && glow !== '#38bdf8' ? glow : '#2563eb',
      accentSoft: 'rgba(37,99,235,0.08)',
      text: '#0f172a',
      subText: '#64748b',
      isLight: true,
    };
  }

  // 2. 浅色系：工业钛灰浅色 (Titanium Light Grey)
  if (style === 'titanium-light') {
    return {
      background: 'linear-gradient(180deg, #f8fafc 0%, #f1f5f9 100%)',
      border: '1px solid #cbd5e1',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow && glow !== '#38bdf8' ? glow : '#0284c7',
      accentSoft: 'rgba(2,132,199,0.1)',
      text: '#1e293b',
      subText: '#475569',
      isLight: true,
    };
  }

  // 3. 深色系：经典石板深灰 (Classic Slate Dark)
  if (style === 'slate-dark') {
    return {
      background: 'linear-gradient(180deg, #1e293b 0%, #0f172a 100%)',
      border: '1px solid #334155',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow || '#38bdf8',
      accentSoft: 'rgba(56,189,248,0.15)',
      text: '#f8fafc',
      subText: '#94a3b8',
      isLight: false,
    };
  }

  // 4. 通透系：悬浮通透胶囊 (Adaptive Frost Capsule)
  if (style === 'translucent-frost') {
    return {
      background: 'rgba(15, 23, 42, 0.82)',
      border: '1px solid rgba(255,255,255,0.15)',
      borderRadius: '8px',
      backdropFilter: 'blur(8px)',
      accent: glow || '#38bdf8',
      accentSoft: 'rgba(56,189,248,0.18)',
      text: '#ffffff',
      subText: '#cbd5e1',
      isLight: false,
    };
  }

  // 兼容旧预设：生态绿 (Eco Green)
  if (style === 'eco-green') {
    return {
      background: 'linear-gradient(180deg, #073a26 0%, #052c1c 55%, #032015 100%)',
      border: '1px solid #064e3b',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow || '#34d399',
      accentSoft: 'rgba(52,211,153,0.16)',
      text: '#eafff5',
      subText: '#7fd9b8',
      isLight: false,
    };
  }

  // 兼容旧预设：机能碳纤橙 (Carbon Orange)
  if (style === 'carbon-orange') {
    return {
      background: 'linear-gradient(180deg, #2a1b0c 0%, #201407 50%, #170d04 100%)',
      border: '1px solid #78350f',
      borderRadius: '2px',
      backdropFilter: 'none',
      accent: glow || '#f59e0b',
      accentSoft: 'rgba(245,158,11,0.14)',
      text: '#fff3e0',
      subText: '#cfaa85',
      isLight: false,
    };
  }

  // 默认（第4种）：深海商务暗蓝 (Navy Midnight / tech-blue)
  return {
    background: 'linear-gradient(180deg, #0b172a 0%, #081a36 60%, #061426 100%)',
    border: '1px solid #1e293b',
    borderRadius: '2px',
    backdropFilter: 'none',
    accent: glow || '#38bdf8',
    accentSoft: 'rgba(56,189,248,0.16)',
    text: '#ffffff',
    subText: '#7dd3fc',
    isLight: false,
  };
});

// ===== 导航菜单图元（nav-menu）：桌面顶部横条 / 移动底部 Tab 栏 =====
// 数据全部来自 props.menuItems（Inspector 编辑，PropsJson 落库），此处仅渲染。
// 跳转不在本组件处理：菜单项带 data-nav-page 标记，由 CanvasPanel 统一分发 navigateToPage。
const menuStyle = computed(() => propOr('menuStyle', 'navy-midnight'));
const menuDevice = computed<'desktop' | 'mobile'>(() =>
  (propOr('menuDevice', 'desktop') as 'desktop' | 'mobile'));
const menuItems = computed<HmiMenuItem[]>(() => {
  const raw = props.component.props.menuItems;
  return Array.isArray(raw) && raw.length
    ? (raw as HmiMenuItem[])
    : (getWidgetDef('nav-menu')?.defaultProps().menuItems as HmiMenuItem[]);
});
const menuAccentColor = computed(() => propOr('menuAccentColor', '#38bdf8'));
const menuFontSize = computed(() => Number(propOr('menuFontSize', 14)));
// 归一化比较：targetPageId 可能是 srv-{serverId}（新配置）或本地 id，currentPageId 亦随会话双轨
const isCurrentMenuItem = (item: HmiMenuItem) =>
  !!item.targetPageId && isSamePageRef(item.targetPageId, props.currentPageId);

// 5 套风格主题计算：桌面顶部导航条 / 移动端底部标签栏
const navMenuTheme = computed(() => {
  const style = menuStyle.value;
  const customAccent = menuAccentColor.value;

  if (style === 'pure-white') {
    const accent = customAccent && customAccent !== '#38bdf8' ? customAccent : '#2563eb';
    return {
      background: '#ffffff',
      border: '1px solid #e2e8f0',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(37,99,235,0.08)',
      itemText: '#64748b',
      activeText: accent,
      isLight: true,
    };
  }

  if (style === 'titanium-light') {
    const accent = customAccent && customAccent !== '#38bdf8' ? customAccent : '#0284c7';
    return {
      background: 'linear-gradient(180deg, #f8fafc 0%, #f1f5f9 100%)',
      border: '1px solid #cbd5e1',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(2,132,199,0.1)',
      itemText: '#475569',
      activeText: accent,
      isLight: true,
    };
  }

  if (style === 'slate-dark') {
    const accent = customAccent || '#38bdf8';
    return {
      background: 'linear-gradient(180deg, #1e293b 0%, #0f172a 100%)',
      border: '1px solid #334155',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(56,189,248,0.15)',
      itemText: '#94a3b8',
      activeText: accent,
      isLight: false,
    };
  }

  if (style === 'translucent-frost') {
    const accent = customAccent || '#38bdf8';
    return {
      background: 'rgba(15, 23, 42, 0.82)',
      border: '1px solid rgba(255,255,255,0.15)',
      backdropFilter: 'blur(8px)',
      accent,
      accentSoft: 'rgba(56,189,248,0.18)',
      itemText: '#cbd5e1',
      activeText: accent,
      isLight: false,
    };
  }

  if (style === 'eco-green') {
    const accent = customAccent && customAccent !== '#38bdf8' ? customAccent : '#34d399';
    return {
      background: 'linear-gradient(180deg, #073a26 0%, #052c1c 55%, #032015 100%)',
      border: '1px solid #064e3b',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(52,211,153,0.16)',
      itemText: '#7fd9b8',
      activeText: accent,
      isLight: false,
    };
  }

  if (style === 'carbon-orange') {
    const accent = customAccent && customAccent !== '#38bdf8' ? customAccent : '#f59e0b';
    return {
      background: 'linear-gradient(180deg, #2a1b0c 0%, #201407 50%, #170d04 100%)',
      border: '1px solid #78350f',
      backdropFilter: 'none',
      accent,
      accentSoft: 'rgba(245,158,11,0.14)',
      itemText: '#cfaa85',
      activeText: accent,
      isLight: false,
    };
  }

  // 默认：深海商务暗蓝 (Navy Midnight)
  const accent = customAccent || '#38bdf8';
  return {
    background: 'linear-gradient(180deg, #0b172a 0%, #081a36 60%, #061426 100%)',
    border: '1px solid #1e293b',
    backdropFilter: 'none',
    accent,
    accentSoft: 'rgba(56,189,248,0.16)',
    itemText: '#9fb6cc',
    activeText: accent,
    isLight: false,
  };
});

// 阶段5-6：text 解耦——开关/阀/数显等有状态文本控件，状态文案改为 props 可配置，默认中文
const onText = computed(() => props.component.props.onText || '开启');
const offText = computed(() => props.component.props.offText || '关闭');

// text 器件动态映射：label 含 {value} 占位符时替换为当前绑定值（兑现图库描述）
const textContent = computed(() => {
  const label = props.component.label ?? '';
  if (!label.includes('{value}')) return label;
  const val = typeof props.value === 'boolean'
    ? (props.value ? onText.value : offText.value)
    : numValue.value.toFixed(2) + (unit.value || '');
  return label.replaceAll('{value}', val);
});

// ===== var-display 数据变量显示 =====
// 小数位数：0~4 钳制，非法值回退 2（与 Inspector 面板/设值弹窗共用同一口径）
const decimals = computed(() => {
  const d = Number(props.component.props.decimals);
  if (!Number.isFinite(d)) return 2;
  return Math.min(4, Math.max(0, Math.round(d)));
});
// 是否可设定：点击弹数字键盘写值（点击分发在 CanvasPanel，此处仅控制角标/光标）
const isSettable = computed(() => props.component.props.settable === true);
// 变量质量非 Good 时显示 -- 而非旧值（配合 CanvasPanel 质量角标）
const qualityBad = computed(() => !!props.quality && props.quality !== 'Good');
const varDisplayText = computed(() => {
  if (qualityBad.value) return '--';
  if (typeof props.value === 'boolean') return props.value ? onText.value : offText.value;
  return numValue.value.toFixed(decimals.value);
});

// 阶段5-6：趋势图过渡占位——未绑定数据源时不绘制伪造曲线，展示占位提示
// 多序列：有效序列（props.trendSeries 或旧式单绑定合成）存在即视为有数据源
const trendSeriesList = computed(() => getEffectiveTrendSeries(props.component));
const trendShowLegend = computed(() => propOr('trendShowLegend', true));
const trendLegendFontSize = computed(() => Number(propOr('trendLegendFontSize', 9)));
const hasTrendData = computed(() => trendSeriesList.value.length > 0);
// 是否任一序列已采到 ≥2 个真实采样点可绘制
const trendReady = computed(() => {
  const map = props.history ?? {};
  return Object.values(map).some((buf) => (buf?.length ?? 0) >= 2);
});

// ===== trend-chart 坐标轴 / 刻度 / 显示增强 配置读取 =====
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

const hasExplicitThresholdMax = computed(() => {
  const v = props.component.props.thresholdMax;
  return v !== undefined && v !== null && !isNaN(Number(v));
});

const hasExplicitThresholdMin = computed(() => {
  const v = props.component.props.thresholdMin;
  return v !== undefined && v !== null && !isNaN(Number(v));
});

const isHighAlert = computed(() => {
  if (typeof props.value === 'boolean') return false;
  if (thresholdMax.value === null || thresholdMax.value === undefined) return false;
  return numValue.value >= thresholdMax.value;
});
const isLowAlert = computed(() => {
  if (typeof props.value === 'boolean') return false;
  if (thresholdMin.value === null || thresholdMin.value === undefined) return false;
  return numValue.value <= thresholdMin.value;
});
const alertColor = computed(() => isHighAlert.value ? '#ef4444' : isLowAlert.value ? '#f59e0b' : activeColor.value);

// var-display 容器样式计算（支持边框颜色、粗细、线条、圆角、背景底色与报警变色）
const varDisplayContainerStyle = computed(() => {
  const p = props.component.props || {};
  const hasBorder = p.showBorder === true || p.showBorder === 'true' as any;
  const hasBg = p.showBackground === true || p.showBackground === 'true' as any;
  const enableAlarm = p.enableAlarmBorder !== false;

  // 检查是否发生报警且启用了报警变色联动（仅当显式配置了有效阈值且产生超限时才触发报警边框）
  const isAlarm = enableAlarm && (
    (hasExplicitThresholdMax.value && isHighAlert.value) ||
    (hasExplicitThresholdMin.value && isLowAlert.value)
  );

  let borderColor = 'transparent';
  let borderWidth = '0px';
  let borderStyle = (p.borderStyle as string) || 'solid';

  if (isAlarm) {
    const bw = p.borderWidth !== undefined && p.borderWidth !== null ? Math.max(2, Number(p.borderWidth)) : 2;
    borderWidth = `${bw}px`;
    borderColor = isHighAlert.value ? '#ef4444' : '#f59e0b';
    borderStyle = 'solid';
  } else if (hasBorder) {
    const bw = p.borderWidth !== undefined && p.borderWidth !== null ? Number(p.borderWidth) : 1.5;
    borderWidth = `${bw}px`;
    borderColor = p.borderColor || p.strokeColor || '#cbd5e1';
    borderStyle = (p.borderStyle as string) || 'solid';
  }

  const borderRadius = p.borderRadius !== undefined && p.borderRadius !== null ? `${p.borderRadius}px` : '8px';
  const backgroundColor = hasBg
    ? (p.bgColor || '#ffffff')
    : 'transparent';

  return {
    borderWidth,
    borderStyle,
    borderColor,
    borderRadius,
    backgroundColor,
    boxSizing: 'border-box' as const,
  };
});

// ===== multi-var-dashboard 实时多变量看板 computed =====
const dashboardTitle = computed(() => propOr('dashboardTitle', '实时参数监控看板'));
const showDashboardTitle = computed(() => propOr('showDashboardTitle', true));
const dashboardTitleBgColor = computed(() => propOr('dashboardTitleBgColor', ''));
const dashboardTitleColor = computed(() => propOr('dashboardTitleColor', ''));
const dashboardLayout = computed<'grid' | 'table' | 'compact'>(() => propOr('dashboardLayout', 'grid'));
const dashboardColumns = computed(() => Number(propOr('dashboardColumns', 2)));
const dashboardGap = computed(() => Number(propOr('dashboardGap', 8)));
const dashboardShowItemBorder = computed(() => propOr('dashboardShowItemBorder', true));
const dashboardItemBorderColor = computed(() => propOr('dashboardItemBorderColor', '#e2e8f0'));
const dashboardItemBgColor = computed(() => propOr('dashboardItemBgColor', '#f8fafc'));
const dashboardValueFontSize = computed(() => Number(propOr('dashboardValueFontSize', 16)));
const dashboardLabelFontSize = computed(() => Number(propOr('dashboardLabelFontSize', 11)));
const dashboardZebra = computed(() => propOr('dashboardZebra', false));

const dashboardItems = computed<HmiDashboardItem[]>(() => {
  const items = props.component.props.dashboardItems;
  if (Array.isArray(items) && items.length > 0) return items;
  const def = defDefaults.value.dashboardItems;
  return Array.isArray(def) ? def : [];
});

// 解析每个多变量子项的实时数据、元数据与报警状态
const dashboardResolvedItems = computed(() => {
  return dashboardItems.value.map((item, idx) => {
    // 优先取 item 本身指定的 deviceId，若无则取组件全局 bindDeviceId，否则取第一个设备
    const devId = item.deviceId != null ? item.deviceId : props.component.bindDeviceId;
    const dev = devId != null
      ? devices.value.find(d => String(d.id) === String(devId))
      : devices.value[0];

    const rawVal = dev?.variables?.[item.variableKey];
    const meta = dev?.variableMeta?.[item.variableKey];
    const label = item.label?.trim() || meta?.name || item.variableKey || `变量 ${idx + 1}`;
    const unit = item.unit !== undefined && item.unit !== '' ? item.unit : (meta?.unit || '');
    const quality = meta?.quality || (dev?.runtimeStatus === 'Offline' ? 'Offline' : 'Good');
    const isQualityBad = quality !== 'Good';

    const isBool = typeof rawVal === 'boolean';
    const isNum = typeof rawVal === 'number';

    let displayVal = '--';
    if (isQualityBad) {
      displayVal = '--';
    } else if (isBool) {
      displayVal = rawVal ? onText.value : offText.value;
    } else if (isNum) {
      const prec = item.precision != null && item.precision !== undefined && item.precision >= 0
        ? Math.min(4, Math.max(0, Math.round(Number(item.precision))))
        : null;
      displayVal = prec !== null ? rawVal.toFixed(prec) : `${rawVal}`;
    } else if (rawVal !== undefined && rawVal !== null) {
      displayVal = String(rawVal);
    }

    const isHigh = !isQualityBad && isNum && item.thresholdMax != null && item.thresholdMax !== undefined && rawVal >= item.thresholdMax;
    const isLow = !isQualityBad && isNum && item.thresholdMin != null && item.thresholdMin !== undefined && rawVal <= item.thresholdMin;
    const isAlarm = isHigh || isLow;

    let statusColor = '#10b981'; // 正常绿
    let statusText = '正常';
    if (isQualityBad) {
      statusColor = '#94a3b8';
      statusText = '离线';
    } else if (isHigh) {
      statusColor = '#ef4444'; // 高限红
      statusText = '高限报警';
    } else if (isLow) {
      statusColor = '#f59e0b'; // 低限黄
      statusText = '低限预警';
    } else if (isBool) {
      statusColor = rawVal ? '#10b981' : '#94a3b8';
      statusText = rawVal ? '运行' : '停止';
    }

    return {
      id: item.id || `item-${idx}`,
      variableKey: item.variableKey,
      label,
      unit,
      rawVal,
      displayVal,
      isBool,
      isNum,
      isQualityBad,
      isHigh,
      isLow,
      isAlarm,
      statusColor,
      statusText,
      showStatusDot: item.showStatusDot !== false,
      devName: dev?.name || '',
    };
  });
});

// 看板整体容器样式
const dashboardContainerStyle = computed(() => {
  const p = props.component.props;
  const hasBorder = p.showBorder !== false;
  const hasBg = p.showBackground !== false;

  const borderWidth = hasBorder ? `${p.borderWidth ?? 1.5}px` : '0px';
  const borderColor = hasBorder ? (p.borderColor || '#cbd5e1') : 'transparent';
  const borderStyle = hasBorder ? (p.borderStyle || 'solid') : 'none';
  const borderRadius = p.borderRadius !== undefined ? `${p.borderRadius}px` : '8px';
  const backgroundColor = hasBg ? (p.bgColor || '#ffffff') : 'transparent';

  return {
    borderWidth,
    borderStyle,
    borderColor,
    borderRadius,
    backgroundColor,
  };
});

// 网格列数样式
const dashboardGridStyle = computed(() => {
  const cols = dashboardColumns.value;
  const gap = `${dashboardGap.value}px`;
  if (cols === 0) {
    return {
      display: 'grid',
      gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))',
      gap,
    };
  }
  return {
    display: 'grid',
    gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`,
    gap,
  };
});

const width = computed(() => props.component.width);
const height = computed(() => props.component.height);

// Dynamic computed states for widget types:
// 1. Pump & Motor rotation angle —— 运行转速随绑定值变化（模拟变频），开启时才转动
const pumpAngle = computed(() =>
  boolValue.value ? (ticks.value * (12 + Math.min(36, Math.abs(numValue.value) / 4))) % 360 : 0
);
const motorAngle = computed(() =>
  boolValue.value ? (ticks.value * (16 + Math.min(48, Math.abs(numValue.value) / 3))) % 360 : 0
);

// 2. Valve handle angle
const valveAngle = computed(() => boolValue.value ? 0 : 90);

// 3. Tank fluid waves
const wavePath = computed(() => {
  const percentHeight = normalizedPercent.value;
  const fluidY = 10 + (100 - percentHeight);
  const waveOffset = props.isActiveMode ? (ticks.value * 0.15) % (2 * Math.PI) : 0;
  return `M 10 ${fluidY} Q 30 ${fluidY - 4 * Math.sin(waveOffset)}, 50 ${fluidY} T 90 ${fluidY} L 90 110 L 10 110 Z`;
});

// 4. Pipe Flow scroll offset for fluid simulation —— 流速随绑定值变化（非恒定常数）
const flowSpeed = computed(() => 0.5 + Math.min(3, Math.abs(numValue.value) / 50));
const pipeScrollOffsetH = computed(() => numValue.value > 0 ? -(ticks.value * flowSpeed.value) % 30 : 0);
const pipeScrollOffsetV = computed(() => numValue.value > 0 ? (ticks.value * flowSpeed.value) % 30 : 0);

// 5. Dial Rotation Angle & Arc Geometry —— 高精度表盘弧度与刻度几何计算
const describeGaugeArc = (cx: number, cy: number, r: number, startAngle: number, endAngle: number) => {
  if (endAngle <= startAngle) return '';
  const rad = (a: number) => (a * Math.PI) / 180;
  const x1 = cx + r * Math.sin(rad(startAngle));
  const y1 = cy - r * Math.cos(rad(startAngle));
  const x2 = cx + r * Math.sin(rad(endAngle));
  const y2 = cy - r * Math.cos(rad(endAngle));
  const diff = endAngle - startAngle;
  const largeArc = diff > 180 ? 1 : 0;
  return `M ${x1.toFixed(2)} ${y1.toFixed(2)} A ${r} ${r} 0 ${largeArc} 1 ${x2.toFixed(2)} ${y2.toFixed(2)}`;
};

const dialAngle = computed(() => {
  const minVal = minValue.value;
  const maxVal = maxValue.value;
  const range = maxVal - minVal;
  if (range <= 0) return -120;
  const boundedVal = Math.max(minVal, Math.min(maxVal, numValue.value));
  return -120 + ((boundedVal - minVal) / range) * 240;
});

const dialTrackArc = computed(() => describeGaugeArc(50, 50, 34, -120, 120));

// 低限预警黄色弧（minValue ~ thresholdMin）
const dialYellowArc = computed(() => {
  const minVal = minValue.value;
  const maxVal = maxValue.value;
  const range = maxVal - minVal;
  if (range <= 0 || thresholdMin.value <= minVal) return '';
  const lowRatio = Math.min(1, Math.max(0, (thresholdMin.value - minVal) / range));
  const lowAngle = -120 + lowRatio * 240;
  if (lowAngle <= -120) return '';
  return describeGaugeArc(50, 50, 34, -120, lowAngle);
});

// 正常安全绿色弧（thresholdMin ~ thresholdMax）
const dialGreenArc = computed(() => {
  const minVal = minValue.value;
  const maxVal = maxValue.value;
  const range = maxVal - minVal;
  if (range <= 0) return '';
  const lowRatio = Math.min(1, Math.max(0, (thresholdMin.value - minVal) / range));
  const highRatio = Math.min(1, Math.max(0, (thresholdMax.value - minVal) / range));
  const startAngle = -120 + lowRatio * 240;
  const endAngle = -120 + highRatio * 240;
  if (endAngle <= startAngle) return '';
  return describeGaugeArc(50, 50, 34, startAngle, endAngle);
});

// 高限危险红色弧（thresholdMax ~ maxValue）
const dialRedArc = computed(() => {
  const minVal = minValue.value;
  const maxVal = maxValue.value;
  const range = maxVal - minVal;
  if (range <= 0) return '';
  const warnRatio = Math.min(1, Math.max(0, (thresholdMax.value - minVal) / range));
  const warnAngle = -120 + warnRatio * 240;
  if (warnAngle >= 120) return '';
  return describeGaugeArc(50, 50, 34, warnAngle, 120);
});

const dialMajorTicks = computed(() => {
  const list = [];
  for (let i = 0; i <= 4; i++) {
    const angle = -120 + (i / 4) * 240;
    const rad = (angle * Math.PI) / 180;
    list.push({
      x1: (50 + 38 * Math.sin(rad)).toFixed(2),
      y1: (50 - 38 * Math.cos(rad)).toFixed(2),
      x2: (50 + 31 * Math.sin(rad)).toFixed(2),
      y2: (50 - 31 * Math.cos(rad)).toFixed(2),
    });
  }
  return list;
});

const dialMinorTicks = computed(() => {
  const list = [];
  for (let i = 0; i <= 16; i++) {
    if (i % 4 === 0) continue;
    const angle = -120 + (i / 16) * 240;
    const rad = (angle * Math.PI) / 180;
    list.push({
      x1: (50 + 37 * Math.sin(rad)).toFixed(2),
      y1: (50 - 37 * Math.cos(rad)).toFixed(2),
      x2: (50 + 34 * Math.sin(rad)).toFixed(2),
      y2: (50 - 34 * Math.cos(rad)).toFixed(2),
    });
  }
  return list;
});

// 6. Trend chart multi-series paths —— 基于 history 真实数据窗口 + 量程归一化（替代伪造正弦波）
// 每条序列独立产出 d 路径与图形属性（颜色/线宽/图例/当前值/报警），支持多变量对比。
/**
 * trend-chart 统一渲染模型：产出共享轴范围、Y/X 刻度、逐序列 path 与点位值标签。
 * - 共享轴优先级：手动范围(trendAxisMin/Max) > 相对模式(0-100%) > 全局自适应 > 逐序列独立(无共享轴)
 * - X 轴以采样时间戳定位（真实相对时间刻度）；无时间跨度时回退等距索引。
 * - 点位值标签自动抽稀（间距 <28px 隔点显示，始终保留最新点）。
 */
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

  // X 时间基准（基于全部序列最新时间戳）
  let tOldest = Infinity, tNewest = -Infinity, nowMs = Date.now();
  for (const s of Object.values(map)) for (const p of s) { if (p.t < tOldest) tOldest = p.t; if (p.t > tNewest) tNewest = p.t; }
  if (Number.isFinite(tNewest) && tNewest > tOldest) nowMs = tNewest;
  const span = (tNewest > tOldest) ? (tNewest - tOldest) : 0;
  const xTicks: { x: number; label: string }[] = [];
  if (showAxisLabels.value && Number.isFinite(tOldest) && span > 0) {
    const N = 4;
    for (let i = 0; i <= N; i++) {
      const frac = i / N;
      xTicks.push({ x: left + frac * innerW, label: relTimeLabel(tOldest + span * frac, nowMs) });
    }
  }

  const seriesOut = series.map((s) => {
    const buf = map[s.id] ?? [];
    let lo = mapLo, hi = mapHi;
    if (!hasShared) {
      const loS = Number(s.minValue), hiS = Number(s.maxValue);
      if (Number.isFinite(loS) && Number.isFinite(hiS) && hiS > loS) { lo = loS; hi = hiS; }
      else if (buf.length) { const vs = buf.map(p => p.v); lo = Math.min(...vs); hi = Math.max(...vs); if (hi <= lo) hi = lo + 1; else { const m = (hi - lo) * 0.1 || 1; lo -= m; hi += m; } }
    }
    const window = buf.slice(-Math.max(2, Math.floor(innerW / 6)));
    const wlen = window.length;
    const xOf = (p: TrendSample) => {
      if (span > 0 && wlen > 1) return left + Math.max(0, Math.min(1, (p.t - tOldest) / span)) * innerW;
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

// 7. Conveyor Speed steps —— 位移周期与箱子间距(80)对齐，消除 step 跳变回退的箱体瞬移
const conveyorBeltStep = computed(() => numValue.value > 0 ? (ticks.value * (numValue.value / 40)) % 80 : 0);

// 8. Auto-updating time for sys-time Clock widget
const currentTime = ref(new Date());
let timeIntervalId: any = null;

onMounted(() => {
  timeIntervalId = setInterval(() => {
    currentTime.value = new Date();
  }, 1000);
});

onUnmounted(() => {
  if (timeIntervalId) {
    clearInterval(timeIntervalId);
  }
});

const timeString = computed(() => {
  const dt = currentTime.value;
  const format = props.component.props.timeFormat || 'HH:mm:ss';

  const pad = (num: number) => num.toString().padStart(2, '0');

  const yyyy = dt.getFullYear();
  const mm = pad(dt.getMonth() + 1);
  const dd = pad(dt.getDate());

  const hh = pad(dt.getHours());
  const min = pad(dt.getMinutes());
  const ss = pad(dt.getSeconds());

  if (format === 'HH:mm:ss') {
    return `${hh}:${min}:${ss}`;
  } else if (format === 'YYYY-MM-DD') {
    return `${yyyy}-${mm}-${dd}`;
  } else {
    return `${yyyy}-${mm}-${dd} ${hh}:${min}:${ss}`;
  }
});

// 9. Rounded Button (圆角按钮) 状态、文本与背景颜色计算
interface StateStyleConfig {
  text: string;
  bgColor: string;
  textColor: string;
  borderColor?: string;
}

const roundedBtnState = computed<StateStyleConfig>(() => {
  const p = props.component.props;
  const rawVal = props.value;
  const strVal = String(rawVal).toLowerCase();
  const isTrueOrNonZero = typeof rawVal === 'boolean' ? rawVal : Number(rawVal) !== 0;

  // 1. 如果配置了 customStates (格式: "0:停止:#334155:#ffffff;1:运行:#0284c7:#ffffff;2:报警:#dc2626:#ffffff")
  if (p.customStates && p.customStates.trim()) {
    try {
      const entries = p.customStates.split(/[;；]/);
      for (const entry of entries) {
        const parts = entry.split(':').map(s => s.trim());
        if (parts.length >= 2) {
          const matchKey = parts[0].toLowerCase();
          if (matchKey === strVal || (matchKey === '1' && strVal === 'true') || (matchKey === '0' && strVal === 'false')) {
            return {
              text: parts[1] || p.buttonText || props.component.label || '按键',
              bgColor: parts[2] || (isTrueOrNonZero ? (p.activeColor || '#0284c7') : (p.inactiveColor || '#1e293b')),
              textColor: parts[3] || '#ffffff',
              borderColor: parts[4] || p.strokeColor || 'transparent',
            };
          }
        }
      }
    } catch (e) {
      console.error('Failed to parse customStates for rounded-btn', e);
    }
  }

  // 2. 如果配置了状态0 / 状态1 的精细配置
  if (isTrueOrNonZero) {
    return {
      text: p.state1Text || p.buttonText || props.component.label || 'ON 运行',
      bgColor: p.state1BgColor || p.activeColor || '#0284c7',
      textColor: p.state1TextColor || '#ffffff',
      borderColor: p.strokeColor || '#38bdf8',
    };
  } else {
    return {
      text: p.state0Text || p.buttonText || props.component.label || 'OFF 停止',
      bgColor: p.state0BgColor || p.inactiveColor || '#1e293b',
      textColor: p.state0TextColor || '#94a3b8',
      borderColor: p.strokeColor || '#475569',
    };
  }
});
</script>

<template>
  <div class="relative w-full h-full">
    <!-- 1. BOILER -->
    <svg v-if="component.type === 'boiler'" width="100%" height="100%" viewBox="0 0 100 120" preserveAspectRatio="none">
      <!-- Boiler Outer Shell -->
      <rect x="10" y="20" width="80" height="90" rx="8" ry="8" fill="#334155" :stroke="strokeColor" stroke-width="3" />
      <!-- Top Chimney -->
      <rect x="40" y="5" width="20" height="15" fill="#475569" :stroke="strokeColor" stroke-width="2" />
      <line x1="35" y1="5" x2="65" y2="5" stroke="#1e293b" stroke-width="3" />

      <!-- Glowing Furnace Window -->
      <circle cx="50" cy="70" r="22" fill="#1e293b" stroke="#475569" stroke-width="2" />

      <!-- Flame Animation when hot or turned on -->
      <path v-if="boolValue" :d="`M 38 78 Q 42 55, 50 ${50 + (ticks % 3) * 2} Q 58 55, 62 78 Q 50 84, 38 78`"
        :fill="isHighAlert ? '#ef4444' : '#f97316'" :opacity="0.8 + Math.sin(ticks * 0.1) * 0.15" />
      <path v-if="boolValue" :d="`M 44 78 Q 46 62, 50 ${60 + (ticks % 2) * 2} Q 54 62, 56 78 Q 50 82, 44 78`"
        fill="#eab308" opacity="0.9" />

      <!-- Analog temperature indicator mini-bar（按量程归一化，maxValue 已防除零） -->
      <rect x="18" y="30" width="6" height="30" rx="3" fill="#1e293b" />
      <rect x="19" :y="30 + (30 - (normalizedPercent / 100) * 30)" width="4" :height="(normalizedPercent / 100) * 30"
        rx="2" :fill="alertColor" />

      <!-- Pressure release outlet -->
      <path d="M 85 40 L 95 40 L 95 48 M 90 40 L 90 35" stroke="#475569" stroke-width="2" fill="none" />
    </svg>

    <!-- 2. PUMP -->
    <svg v-else-if="component.type === 'pump'" width="100%" height="100%" viewBox="0 0 80 80"
      preserveAspectRatio="none">
      <!-- Pump Support Stand -->
      <rect x="10" y="65" width="60" height="10" fill="#334155" rx="2" />
      <rect x="25" y="55" width="30" height="10" fill="#475569" />

      <!-- Main circular casing -->
      <circle cx="40" cy="35" r="28" :fill="boolValue ? '#1e293b' : '#334155'"
        :stroke="boolValue ? alertColor : strokeColor" stroke-width="4" />

      <!-- Tangential water pipe connection -->
      <path d="M 68 20 L 78 20 L 78 30" :stroke="strokeColor" stroke-width="3" fill="none" />

      <!-- Rotating rotor blades -->
      <g :transform="`translate(40, 35) rotate(${pumpAngle})`">
        <circle cx="0" cy="0" r="6" fill="#64748b" />
        <line x1="-22" y1="0" x2="22" y2="0" :stroke="boolValue ? alertColor : '#94a3b8'" stroke-width="4" />
        <line x1="0" y1="-22" x2="0" y2="22" :stroke="boolValue ? alertColor : '#94a3b8'" stroke-width="4" />
        <circle cx="-22" cy="0" r="3.5" fill="#475569" />
        <circle cx="22" cy="0" r="3.5" fill="#475569" />
        <circle cx="0" cy="-22" r="3.5" fill="#475569" />
        <circle cx="0" cy="22" r="3.5" fill="#475569" />
      </g>

      <!-- Small operational dynamic indicator -->
      <circle cx="18" cy="18" r="4" :fill="boolValue ? '#10b981' : '#ef4444'" />
    </svg>

    <!-- 3. VALVE -->
    <svg v-else-if="component.type === 'valve'" width="100%" height="100%" viewBox="0 0 80 80"
      preserveAspectRatio="none">
      <!-- Pipe Flanges -->
      <rect x="5" y="30" width="8" height="20" fill="#475569" />
      <rect x="67" y="30" width="8" height="20" fill="#475569" />

      <!-- Valves Body Triangles -->
      <polygon points="12,25 12,55 40,40" :fill="boolValue ? activeColor : inactiveColor" stroke="#334155"
        stroke-width="2" />
      <polygon points="68,25 68,55 40,40" :fill="boolValue ? activeColor : inactiveColor" stroke="#334155"
        stroke-width="2" />

      <!-- Center Seal/Shaft -->
      <circle cx="40" cy="40" r="10" fill="#1e293b" stroke="#334155" stroke-width="2" />
      <rect x="36" y="16" width="8" height="15" fill="#64748b" />

      <!-- Rotatable handle -->
      <g :transform="`translate(40, 16) rotate(${valveAngle})`">
        <line x1="-18" y1="0" x2="18" y2="0" stroke="#ef4444" stroke-width="4" />
        <circle cx="-18" cy="0" r="3" fill="#334155" />
        <circle cx="18" cy="0" r="3" fill="#334155" />
      </g>

      <!-- Status Text panel overlay -->
      <rect x="18" y="60" width="44" height="15" rx="3" fill="#1e293b" opacity="0.9" />
      <text x="40" y="71" :fill="boolValue ? '#10b981' : '#f43f5e'" font-size="9" text-anchor="middle"
        font-weight="bold">
        {{ boolValue ? onText : offText }}
      </text>
    </svg>

    <!-- 4. TANK -->
    <svg v-else-if="component.type === 'tank'" width="100%" height="100%" viewBox="0 0 100 120"
      preserveAspectRatio="none">
      <!-- Leg supports -->
      <line x1="20" y1="110" x2="15" y2="118" stroke="#475569" stroke-width="4" />
      <line x1="80" y1="110" x2="85" y2="118" stroke="#475569" stroke-width="4" />

      <!-- Main Glass Body container -->
      <rect x="8" y="8" width="84" height="104" rx="10" ry="10" fill="#1e293b" :stroke="strokeColor" stroke-width="3" />

      <!-- Wave flow surface -->
      <path v-if="numValue > 0" :d="normalizedPercent >= 99 ? 'M 10 10 L 90 10 L 90 110 L 10 110 Z' : wavePath"
        :fill="fillColor || '#3b82f6'" opacity="0.8" />

      <!-- Glossy Highlight -->
      <rect x="12" y="12" width="12" height="96" rx="4" fill="#ffffff" opacity="0.08" />

      <!-- Grid Overlay lines -->
      <g stroke="#ffffff" stroke-width="1" opacity="0.25">
        <line x1="10" y1="35" x2="25" y2="35" />
        <line x1="10" y1="60" x2="30" y2="60" />
        <line x1="10" y1="85" x2="25" y2="85" />

        <line x1="90" y1="35" x2="75" y2="35" />
        <line x1="90" y1="60" x2="70" y2="60" />
        <line x1="90" y1="85" x2="75" y2="85" />
      </g>

      <!-- Numeric Value Overlay（显示原始值+单位，而非强制百分比） -->
      <text x="50" y="65" text-anchor="middle" fill="#ffffff" font-size="11" font-weight="bold" stroke="#000"
        stroke-width="1" paint-order="stroke">
        {{ numValue.toFixed(1) }}{{ unit ? ' ' + unit : '' }}
      </text>
    </svg>

    <!-- 5. PIPE HORIZONTAL -->
    <div v-else-if="component.type === 'pipe-h'" class="w-full h-full relative flex items-center">
      <div class="absolute inset-x-0 h-4 rounded-full border-t border-b overflow-hidden shadow-inner flex items-center"
        :style="{
          backgroundColor: numValue > 0 ? '#334155' : inactiveColor,
          borderColor: strokeColor,
        }">
        <div v-if="numValue > 0" class="w-[200%] h-1 opacity-70" :style="{
          backgroundImage: `repeating-linear-gradient(90deg, transparent, transparent 15px, ${activeColor} 15px, ${activeColor} 30px)`,
          transform: `translateX(${pipeScrollOffsetH}px)`,
        }" />
      </div>
      <div class="absolute left-0 top-0 bottom-0 w-2 bg-slate-700 rounded-sm border-r border-slate-500" />
      <div class="absolute right-0 top-0 bottom-0 w-2 bg-slate-700 rounded-sm border-l border-slate-500" />
    </div>

    <!-- 6. PIPE VERTICAL -->
    <div v-else-if="component.type === 'pipe-v'" class="w-full h-full relative flex justify-center">
      <div
        class="absolute inset-y-0 w-4 rounded-full border-l border-r overflow-hidden shadow-inner flex justify-center"
        :style="{
          backgroundColor: numValue > 0 ? '#334155' : inactiveColor,
          borderColor: strokeColor,
        }">
        <div v-if="numValue > 0" class="h-[200%] w-1 opacity-70" :style="{
          backgroundImage: `repeating-linear-gradient(180deg, transparent, transparent 15px, ${activeColor} 15px, ${activeColor} 30px)`,
          transform: `translateY(${pipeScrollOffsetV}px)`,
        }" />
      </div>
      <div class="absolute top-0 left-0 right-0 h-2 bg-slate-700 rounded-sm border-b border-slate-500" />
      <div class="absolute bottom-0 left-0 right-0 h-2 bg-slate-700 rounded-sm border-t border-slate-500" />
    </div>

    <!-- 7. circular dial gauge -->
    <svg v-else-if="component.type === 'gauge-dial'" width="100%" height="100%" viewBox="0 0 100 100"
      preserveAspectRatio="xMidYMid meet" class="select-none">
      <!-- Bezel & Face -->
      <circle cx="50" cy="50" r="47" fill="#1e293b" stroke="#334155" stroke-width="2.5" />
      <circle cx="50" cy="50" r="43" fill="#0f172a" stroke="#1e293b" stroke-width="1" />

      <!-- Background Track Arc -->
      <path v-if="dialTrackArc" :d="dialTrackArc" stroke="#334155" stroke-width="3" fill="none"
        stroke-linecap="round" />

      <!-- Low Warning Zone Yellow Arc -->
      <path v-if="dialYellowArc" :d="dialYellowArc" stroke="#f59e0b" stroke-width="3" fill="none"
        stroke-linecap="round" />

      <!-- Normal/Safe Zone Green Arc -->
      <path v-if="dialGreenArc" :d="dialGreenArc" stroke="#10b981" stroke-width="3" fill="none"
        stroke-linecap="round" />

      <!-- High Warning/Danger Zone Red Arc -->
      <path v-if="dialRedArc" :d="dialRedArc" stroke="#ef4444" stroke-width="3" fill="none" stroke-linecap="round" />

      <!-- Minor Ticks -->
      <g stroke="#475569" stroke-width="1">
        <line v-for="(t, i) in dialMinorTicks" :key="'min-' + i" :x1="t.x1" :y1="t.y1" :x2="t.x2" :y2="t.y2" />
      </g>

      <!-- Major Ticks -->
      <g stroke="#94a3b8" stroke-width="1.5">
        <line v-for="(t, i) in dialMajorTicks" :key="'maj-' + i" :x1="t.x1" :y1="t.y1" :x2="t.x2" :y2="t.y2" />
      </g>

      <!-- Scale Min/Max labels -->
      <text x="21" y="74" text-anchor="middle" fill="#64748b" font-size="5.5" font-family="monospace">
        {{ minValue }}
      </text>
      <text x="79" y="74" text-anchor="middle" fill="#64748b" font-size="5.5" font-family="monospace">
        {{ maxValue }}
      </text>

      <!-- Center Label -->
      <text x="50" y="65" text-anchor="middle" fill="#94a3b8" font-size="7" font-weight="500">
        {{ component.label }}
      </text>

      <!-- Value & Unit Text -->
      <text x="50" y="79" text-anchor="middle" :fill="alertColor" font-size="9.5" font-weight="bold"
        font-family="monospace">
        {{ numValue.toFixed(1) }}<tspan font-size="6.5" fill="#64748b" dx="1">{{ unit || '' }}</tspan>
      </text>

      <!-- Needle -->
      <g :transform="`translate(50, 50) rotate(${dialAngle})`" class="transition-transform duration-300 ease-out">
        <!-- Shadow -->
        <path d="M -1.5 0 L 0 -34 L 1.5 0 Z" fill="#000000" opacity="0.3" transform="translate(0.5, 0.5)" />
        <!-- Needle Pointer -->
        <path d="M -2 0 L 0 -34 L 2 0 L 1.2 5 L -1.2 5 Z" :fill="alertColor" stroke="#0f172a" stroke-width="0.4" />
        <!-- Center Pivot Cap -->
        <circle cx="0" cy="0" r="4" fill="#334155" stroke="#64748b" stroke-width="1" />
        <circle cx="0" cy="0" r="1.8" fill="#f8fafc" />
      </g>
    </svg>

    <!-- 8. LEVEL BAR -->
    <div v-else-if="component.type === 'gauge-level'"
      class="w-full h-full flex flex-col items-center bg-slate-900 border border-slate-700 rounded p-1 font-mono text-[9px] text-slate-400">
      <div
        class="flex-1 w-full bg-slate-950 border border-slate-800 rounded relative overflow-hidden flex flex-col justify-end">
        <div class="w-full transition-all duration-300" :style="{
          height: `${normalizedPercent}%`,
          backgroundColor: alertColor,
          boxShadow: `0 0 12px ${alertColor}`,
        }" />
        <div
          class="absolute inset-0 flex flex-col justify-between p-1 opacity-50 pointer-events-none text-[8px] text-slate-300 font-mono">
          <span>H</span>
          <span>M</span>
          <span>L</span>
        </div>
      </div>
      <div class="mt-1 text-[10px] text-slate-100 font-bold truncate max-w-full">
        {{ numValue.toFixed(0) }}{{ unit ? ' ' + unit : '' }}
      </div>
    </div>

    <!-- 9. DIGITAL VALUE -->
    <div v-else-if="component.type === 'digital-val'"
      class="w-full h-full bg-slate-950 border-2 rounded-lg flex flex-col justify-center items-center px-2 py-1 shadow-inner relative overflow-hidden"
      :style="{ borderColor: isHighAlert ? '#ef4444' : '#1e293b' }">
      <div class="absolute top-1 left-2 text-[8px] text-slate-400 uppercase tracking-widest font-mono">
        {{ component.label || '数字监测' }}
      </div>
      <div class="text-xl md:text-2xl font-black mt-2 font-mono tracking-widest"
        :style="{ color: isHighAlert ? '#ef4444' : '#34d399' }">
        {{ typeof value === 'boolean' ? (boolValue ? onText : offText) : `${numValue.toFixed(2)}` }}
        <span v-if="typeof value !== 'boolean' && unit" class="text-xs text-slate-500 font-normal ml-0.5">{{ unit
        }}</span>
      </div>
      <div class="absolute bottom-1 right-2 w-1.5 h-1.5 rounded-full" :style="{
        backgroundColor: boolValue ? '#10b981' : '#ef4444',
        animation: boolValue ? 'pulse 1.2s infinite' : 'none',
      }" />
    </div>

    <!-- 9.5 VAR DISPLAY（数据变量显示） -->
    <!-- 外观显隐：边框/背景/内部标签独立开关；支持边框颜色、粗细、圆角及报警指示 -->
    <div v-else-if="component.type === 'var-display'"
      class="w-full h-full flex flex-col justify-center items-center px-3 py-1 relative overflow-hidden select-none transition-all duration-150"
      :class="[
        isActiveMode && isSettable && !isLockedControl ? 'cursor-pointer hover:shadow-md' : '',
        showBackground && !component.props.bgColor ? 'bg-white dark:bg-slate-950' : '',
      ]" :style="varDisplayContainerStyle">
      <div v-if="showInnerLabel"
        class="absolute top-1 left-2.5 text-[9px] text-slate-400 dark:text-slate-500 truncate max-w-[80%] font-mono pointer-events-none">
        {{ component.label || '变量' }}
      </div>
      <div class="font-mono font-bold tracking-wide leading-none tabular-nums" :class="showInnerLabel ? 'mt-1.5' : ''"
        :style="{
          fontSize: `${fontSize * 1.6}px`,
          color: (component.props.enableAlarmBorder !== false && hasExplicitThresholdMax && isHighAlert) ? '#ef4444' : ((component.props.enableAlarmBorder !== false && hasExplicitThresholdMin && isLowAlert) ? '#f59e0b' : (qualityBad ? '#94a3b8' : activeColor))
        }">
        {{ varDisplayText }}
        <span v-if="typeof value === 'number' && unit && !qualityBad"
          class="text-xs font-normal text-slate-400 dark:text-slate-500 ml-0.5">{{ unit }}</span>
      </div>
      <!-- 可设定角标：提示操作员可点击写值 -->
      <span v-if="isSettable && isActiveMode"
        class="absolute bottom-1 right-1.5 text-[9px] leading-none pointer-events-none"
        :class="isLockedControl ? 'text-amber-500' : 'text-sky-500 dark:text-sky-400'">
        {{ isLockedControl ? '只读' : '✎' }}
      </span>
      <!-- 运行模式无写权限：绑定显示组件显示只读锁标记（与按钮口径一致） -->
      <span v-if="isLockedControl && !isSettable"
        class="absolute bottom-1 left-1.5 text-[8px] text-amber-500 flex items-center gap-0.5 leading-none"
        title="当前角色无写权限，控件为只读">
        <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor">
          <path
            d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5zm3 8H9V6a3 3 0 0 1 6 0v3z" />
        </svg>
        只读
      </span>
    </div>

    <!-- 10. REAL-TIME TREND CHART (multi-series + 坐标轴/刻度/点位值) -->
    <div v-else-if="component.type === 'trend-chart'"
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

    <!-- 11. CONVEYOR BELT -->
    <svg v-else-if="component.type === 'conveyor'" width="100%" height="100%" viewBox="0 0 300 40"
      preserveAspectRatio="none">
      <rect x="5" y="12" width="290" height="16" rx="8" fill="#1e293b" :stroke="strokeColor" stroke-width="2.5" />
      <circle cx="20" cy="20" r="6" fill="#64748b" stroke="#334155" />
      <circle cx="20" cy="20" r="2" fill="#1e293b" />
      <circle cx="105" cy="20" r="6" fill="#64748b" stroke="#334155" />
      <circle cx="105" cy="20" r="2" fill="#1e293b" />
      <circle cx="195" cy="20" r="6" fill="#64748b" stroke="#334155" />
      <circle cx="195" cy="20" r="2" fill="#1e293b" />
      <circle cx="280" cy="20" r="6" fill="#64748b" stroke="#334155" />
      <circle cx="280" cy="20" r="2" fill="#1e293b" />

      <g v-if="numValue > 0">
        <rect :x="20 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
        <rect :x="100 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
        <rect :x="180 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
        <rect :x="260 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
      </g>
      <line x1="8" y1="31" x2="292" y2="31" stroke="#475569" stroke-width="2" stroke-dasharray="4 4" />
    </svg>

    <!-- 12. TEXT LABEL -->
    <div v-else-if="component.type === 'text'" class="w-full h-full flex items-center" :style="{
      justifyContent: align === 'center' ? 'center' : align === 'right' ? 'flex-end' : 'flex-start',
      fontSize: `${fontSize}px`,
      fontWeight: bold ? 'bold' : 'normal',
      color: activeColor || '#cbd5e1',
    }">
      {{ textContent }}
    </div>

    <!-- 13. LED INDICATOR -->
    <div v-else-if="component.type === 'led'" class="w-full h-full flex flex-col items-center justify-center">
      <div class="rounded-full transition-all duration-300" :style="{
        width: `${Math.min(width, height) - 12}px`,
        height: `${Math.min(width, height) - 12}px`,
        backgroundColor: boolValue ? activeColor : inactiveColor,
        boxShadow: boolValue ? `0 0 16px ${activeColor}, inset 0 2px 4px rgba(255,255,255,0.4)` : 'inset 0 2px 4px rgba(0,0,0,0.4)',
        border: '3px solid #334155',
      }" />
      <span class="text-[9px] text-slate-300 font-mono mt-1 text-center truncate max-w-full">
        {{ component.label }}
      </span>
      <span class="text-[8px] text-slate-500 dark:text-slate-400 font-mono text-center truncate max-w-full"
        v-if="component.bindVariableKey || component.bindField">
        {{ boolValue ? onText : offText }}
      </span>
    </div>

    <!-- 14. INDUSTRIAL BUTTON -->
    <div v-else-if="component.type === 'button'" class="w-full h-full p-0.5">
      <div
        class="w-full h-full rounded border-2 shadow flex flex-col items-center justify-center transition-all select-none relative overflow-hidden"
        :class="[
          isActiveMode ? 'active:translate-y-0.5 active:shadow-inner cursor-pointer' : '',
          boolValue ? 'shadow-inner' : 'shadow-md border-t-white border-l-white border-b-slate-900 border-r-slate-900'
        ]" :style="{
          backgroundColor: boolValue ? activeColor : fillColor || '#cbd5e1',
          borderColor: boolValue ? (strokeColor || '#0284c7') : '#94a3b8',
          color: boolValue ? '#ffffff' : '#1e293b'
        }">
        <!-- Led Indicator inside the button -->
        <div class="absolute top-1 right-2 w-1.5 h-1.5 rounded-full border border-slate-600/30" :style="{
          backgroundColor: boolValue ? '#22c55e' : '#dc2626',
          boxShadow: boolValue ? '0 0 6px #22c55e' : 'none'
        }" />
        <!-- 阶段6-2：运行模式无写权限时，绑定按钮显示只读锁标记 -->
        <span v-if="isLockedControl"
          class="absolute bottom-1 left-1.5 text-[8px] text-amber-500 flex items-center gap-0.5 leading-none"
          title="当前角色无写权限，控件为只读">
          <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor">
            <path
              d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5zm3 8H9V6a3 3 0 0 1 6 0v3z" />
          </svg>
          只读
        </span>
        <!-- Label -->
        <span class="text-center font-mono pointer-events-none px-1 truncate max-w-full" :style="{
          fontSize: `${fontSize}px`,
          fontWeight: bold ? 'bold' : 'normal'
        }">
          {{ component.props.buttonText || component.label || '命令键' }}
        </span>
        <span class="text-[8px] opacity-60 font-sans pointer-events-none mt-0.5 select-none"
          v-if="component.bindVariableKey || component.bindField">
          {{ component.props.buttonMode === 'momentary' ? '[点动]' :
            component.props.buttonMode === 'set-bit' ? '[置位]' :
              component.props.buttonMode === 'reset-bit' ? '[复位]' :
                component.props.buttonMode === 'set-value' ? `[设值:${component.props.clickValue ?? 0}]` :
                  component.props.buttonMode === 'navigate' ? '[跳转]' : '[自锁]' }}
        </span>
      </div>
    </div>

    <!-- 15. TOGGLE SWITCH -->
    <div v-else-if="component.type === 'switch'"
      class="w-full h-full flex flex-col items-center justify-center p-1 font-mono text-[9px] select-none">
      <div
        class="w-full h-full bg-[#1e293b] border border-slate-700 rounded p-1.5 flex flex-col items-center justify-between shadow-md relative">
        <!-- 阶段6-2：运行模式无写权限时，绑定开关显示只读锁标记 -->
        <span v-if="isLockedControl"
          class="absolute top-1 right-1.5 text-[8px] text-amber-500 flex items-center gap-0.5 leading-none"
          title="当前角色无写权限，控件为只读">
          <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor">
            <path
              d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5zm3 8H9V6a3 3 0 0 1 6 0v3z" />
          </svg>
          只读
        </span>
        <!-- Top State Label -->
        <span class="text-slate-400 font-bold uppercase text-[8px] tracking-tight text-center truncate max-w-full">
          {{ boolValue ? onText : offText }}
        </span>

        <!-- Slot slider & Lever knob style-->
        <div
          class="w-6 h-10 bg-slate-950 rounded-full border border-slate-800 relative flex items-center justify-center overflow-hidden shadow-inner cursor-pointer">
          <div
            class="w-5 h-5 rounded-full bg-slate-300 border border-slate-500 shadow-md transition-all duration-300 flex items-center justify-center"
            :style="{
              transform: boolValue ? 'translateY(-10px)' : 'translateY(10px)',
              backgroundColor: boolValue ? '#10b981' : '#ef4444',
              boxShadow: boolValue ? '0 2px 4px rgba(16,185,129,0.4)' : '0 2px 4px rgba(239,68,68,0.4)',
            }">
            <div class="w-1.5 h-1.5 rounded-full bg-white/60" />
          </div>
        </div>

        <!-- Bottom Label text -->
        <span class="text-slate-500 text-[8px] font-bold text-center truncate max-w-full">
          {{ component.label }}
        </span>
      </div>
    </div>

    <!-- 16. SYSTEM CLOCK / TIME -->
    <div v-else-if="component.type === 'sys-time'"
      class="w-full h-full bg-black/90 border-2 border-slate-800 rounded-lg flex flex-col justify-center items-center px-2 py-1 shadow-inner relative text-emerald-400 font-mono select-none">
      <div class="absolute top-1 left-2 text-[8px] text-slate-500 uppercase tracking-widest leading-none">
        {{ component.label || 'SYSTEM CLOCK' }}
      </div>
      <!-- Interactive time ticking -->
      <div class="text-[13px] sm:text-[14px] font-bold text-center mt-2.5 tracking-wider w-full truncate"
        v-text="timeString" />
      <div class="absolute bottom-1 right-2 flex items-center gap-1">
        <span class="w-1 h-1 rounded-full bg-emerald-500 animate-pulse" />
        <span class="text-[7px] text-slate-500">LIVE</span>
      </div>
    </div>

    <!-- 18. INDUSTRIAL ROUNDED BUTTON (圆角按钮组件) -->
    <div v-else-if="component.type === 'rounded-btn'" class="w-full h-full p-0.5">
      <div
        class="w-full h-full shadow flex flex-col items-center justify-center transition-all select-none relative overflow-hidden group"
        :class="[
          isActiveMode ? 'active:scale-95 active:brightness-90 cursor-pointer' : '',
          boolValue ? 'shadow-md' : 'shadow-xs'
        ]" :style="{
          borderRadius: `${component.props.borderRadius ?? 10}px`,
          borderWidth: `${component.props.borderWidth ?? 1}px`,
          borderColor: roundedBtnState.borderColor || component.props.strokeColor || '#38bdf8',
          backgroundColor: roundedBtnState.bgColor,
          color: roundedBtnState.textColor,
        }">
        <!-- Subtle top gloss highlight for industrial tactility -->
        <div class="absolute inset-x-0 top-0 h-1/2 bg-gradient-to-b from-white/20 to-transparent pointer-events-none"
          :style="{ borderTopLeftRadius: `${component.props.borderRadius ?? 10}px`, borderTopRightRadius: `${component.props.borderRadius ?? 10}px` }" />

        <!-- Status LED dot -->
        <div class="absolute top-1.5 right-2 w-2 h-2 rounded-full border border-black/20" :style="{
          backgroundColor: boolValue ? '#22c55e' : '#64748b',
          boxShadow: boolValue ? '0 0 8px #22c55e' : 'none'
        }" />

        <!-- 只读锁标记 -->
        <span v-if="isLockedControl"
          class="absolute bottom-1 left-2 text-[8px] text-amber-300 flex items-center gap-0.5 leading-none bg-black/40 px-1 py-0.5 rounded"
          title="当前角色无写权限，控件为只读">
          <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor">
            <path
              d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5zm3 8H9V6a3 3 0 0 1 6 0v3z" />
          </svg>
          只读
        </span>

        <!-- Primary Label / Dynamic text -->
        <span class="text-center font-mono pointer-events-none px-2 truncate max-w-full z-10 drop-shadow-xs" :style="{
          fontSize: `${fontSize}px`,
          fontWeight: bold ? 'bold' : '600'
        }">
          {{ roundedBtnState.text }}
        </span>

        <!-- Mode badge / hint（可配置隐藏：props.showModeBadge === false 时不渲染） -->
        <span v-if="component.props.showModeBadge !== false"
          class="text-[8px] opacity-75 font-sans pointer-events-none mt-0.5 select-none z-10">
          {{ component.props.buttonMode === 'momentary' ? '[按1送0]' : component.props.buttonMode === 'set-bit' ? '[置位1]'
            :
            component.props.buttonMode === 'reset-bit' ? '[复位0]' : component.props.buttonMode === 'set-value' ?
              `[设值:${component.props.clickValue ?? 0}]` : component.props.buttonMode === 'navigate' ? '[跳转]' :
                component.props.buttonMode === 'run-script' ? '[脚本]' : '[取反]' }}
        </span>
      </div>
    </div>

    <!-- 19. INDUSTRIAL VARIABLE FREQUENCY AC SERVO MOTOR (变频伺服AC电机) -->
    <svg v-else-if="component.type === 'motor'" width="100%" height="100%" viewBox="0 0 120 90"
      preserveAspectRatio="xMidYMid meet" class="select-none overflow-visible">
      <defs>
        <!-- Stator Cylindrical Metal Gradient -->
        <linearGradient :id="'motor-stator-' + component.id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#334155" />
          <stop offset="18%" stop-color="#475569" />
          <stop offset="50%" stop-color="#1e293b" />
          <stop offset="85%" stop-color="#0f172a" />
          <stop offset="100%" stop-color="#1e293b" />
        </linearGradient>

        <!-- Front / Rear Flange Gradient -->
        <linearGradient :id="'motor-flange-' + component.id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#64748b" />
          <stop offset="30%" stop-color="#94a3b8" />
          <stop offset="70%" stop-color="#334155" />
          <stop offset="100%" stop-color="#1e293b" />
        </linearGradient>

        <!-- Stainless Steel Shaft Gradient -->
        <linearGradient :id="'motor-shaft-' + component.id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#64748b" />
          <stop offset="25%" stop-color="#cbd5e1" />
          <stop offset="45%" stop-color="#f8fafc" />
          <stop offset="75%" stop-color="#94a3b8" />
          <stop offset="100%" stop-color="#475569" />
        </linearGradient>

        <!-- Rear Fan Cowl Gradient -->
        <linearGradient :id="'motor-cowl-' + component.id" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="#1e293b" />
          <stop offset="60%" stop-color="#334155" />
          <stop offset="100%" stop-color="#1e293b" />
        </linearGradient>

        <!-- Terminal Box Gradient -->
        <linearGradient :id="'motor-tbox-' + component.id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#475569" />
          <stop offset="40%" stop-color="#334155" />
          <stop offset="100%" stop-color="#1e293b" />
        </linearGradient>
      </defs>

      <!-- Base / Footings (Cast Iron Machine Feet) -->
      <g>
        <!-- Anti-vibration damper line -->
        <rect x="22" y="80" width="76" height="3" rx="1" fill="#0f172a" opacity="0.8" />
        <!-- Left Foot -->
        <path d="M 28 66 L 24 79 L 46 79 L 43 66 Z" fill="#334155" stroke="#1e293b" stroke-width="1" />
        <rect x="26" y="74" width="18" height="5.5" rx="1.5" fill="#1e293b" />
        <!-- Bolt Hole & Hex Bolt Left -->
        <circle cx="35" cy="76.5" r="2.5" fill="#0f172a" />
        <circle cx="35" cy="76.5" r="1.5" fill="#94a3b8" />

        <!-- Right Foot -->
        <path d="M 77 66 L 74 79 L 96 79 L 92 66 Z" fill="#334155" stroke="#1e293b" stroke-width="1" />
        <rect x="76" y="74" width="18" height="5.5" rx="1.5" fill="#1e293b" />
        <!-- Bolt Hole & Hex Bolt Right -->
        <circle cx="85" cy="76.5" r="2.5" fill="#0f172a" />
        <circle cx="85" cy="76.5" r="1.5" fill="#94a3b8" />

        <!-- Machine Base Connecting Bar -->
        <rect x="25" y="66" width="70" height="5" fill="#1e293b" stroke="#334155" stroke-width="0.75" />
      </g>

      <!-- Drive Shaft & Output Flange (Left) -->
      <g>
        <!-- Main Output Shaft -->
        <rect x="4" y="42" width="22" height="14" rx="1.5" :fill="`url(#motor-shaft-${component.id})`" stroke="#475569"
          stroke-width="0.75" />
        <line x1="4" y1="46" x2="26" y2="46" stroke="#ffffff" stroke-width="1" opacity="0.6" />

        <!-- Keyway & Shaft Rotation dynamic tick (Spinning when running) -->
        <rect x="7" y="44.5" width="10" height="3" rx="0.5" fill="#334155" opacity="0.7" />
        <g :transform="`translate(9, 49) rotate(${motorAngle})`">
          <circle cx="0" cy="0" r="3" fill="#0f172a" opacity="0.4" />
          <line x1="-3" y1="0" x2="3" y2="0" :stroke="boolValue ? activeColor : '#94a3b8'" stroke-width="1.5"
            stroke-linecap="round" />
        </g>

        <!-- Shaft Coupling Collar / Step -->
        <rect x="20" y="39" width="6" height="20" rx="1" :fill="`url(#motor-flange-${component.id})`" stroke="#1e293b"
          stroke-width="0.75" />

        <!-- Front Drive End-Shield / Flange (前轴承盖法兰) -->
        <rect x="26" y="24" width="8" height="50" rx="2" :fill="`url(#motor-flange-${component.id})`" stroke="#1e293b"
          stroke-width="1" />
        <!-- Flange Mounting Hex Bolts -->
        <circle cx="30" cy="28" r="1.5" fill="#cbd5e1" stroke="#334155" stroke-width="0.5" />
        <circle cx="30" cy="38" r="1.5" fill="#cbd5e1" stroke="#334155" stroke-width="0.5" />
        <circle cx="30" cy="60" r="1.5" fill="#cbd5e1" stroke="#334155" stroke-width="0.5" />
        <circle cx="30" cy="70" r="1.5" fill="#cbd5e1" stroke="#334155" stroke-width="0.5" />
      </g>

      <!-- Stator Housing (Main Body) & Aluminum Cooling Ribs -->
      <g>
        <!-- Stator Barrel Core -->
        <rect x="34" y="19" width="56" height="60" rx="3" :fill="`url(#motor-stator-${component.id})`"
          :stroke="boolValue ? alertColor : '#334155'" :stroke-width="boolValue ? 1.5 : 1" />

        <!-- Running Electromagnetic Field Aura / Active Glow -->
        <rect v-if="boolValue" x="33" y="18" width="58" height="62" rx="4" fill="none" :stroke="alertColor"
          stroke-width="1" opacity="0.4" />

        <!-- 7 Precision Cooling Fins (散热肋片) with light/shadow edges -->
        <g stroke-linecap="round">
          <!-- Fin 1 -->
          <line x1="35" y1="23" x2="89" y2="23" stroke="#475569" stroke-width="2" />
          <line x1="35" y1="24.5" x2="89" y2="24.5" stroke="#0f172a" stroke-width="1" />
          <!-- Fin 2 -->
          <line x1="35" y1="29" x2="89" y2="29" stroke="#475569" stroke-width="2" />
          <line x1="35" y1="30.5" x2="89" y2="30.5" stroke="#0f172a" stroke-width="1" />
          <!-- Fin 3 -->
          <line x1="35" y1="35" x2="89" y2="35" stroke="#475569" stroke-width="2" />
          <line x1="35" y1="36.5" x2="89" y2="36.5" stroke="#0f172a" stroke-width="1" />
          <!-- Fin 4 -->
          <line x1="35" y1="62" x2="89" y2="62" stroke="#475569" stroke-width="2" />
          <line x1="35" y1="63.5" x2="89" y2="63.5" stroke="#0f172a" stroke-width="1" />
          <!-- Fin 5 -->
          <line x1="35" y1="68" x2="89" y2="68" stroke="#475569" stroke-width="2" />
          <line x1="35" y1="69.5" x2="89" y2="69.5" stroke="#0f172a" stroke-width="1" />
          <!-- Fin 6 -->
          <line x1="35" y1="74" x2="89" y2="74" stroke="#475569" stroke-width="2" />
          <line x1="35" y1="75.5" x2="89" y2="75.5" stroke="#0f172a" stroke-width="1" />
        </g>

        <!-- Center Nameplate Badge (工业铭牌面板) -->
        <rect x="42" y="39" width="40" height="20" rx="2" fill="#090d16" stroke="#334155" stroke-width="1" />
        <!-- Label / Title -->
        <text x="62" y="46" text-anchor="middle" fill="#94a3b8" font-size="5.5" font-weight="600"
          font-family="sans-serif">
          {{ component.label || 'AC SERVO' }}
        </text>
        <!-- Dynamic Speed / State Readout -->
        <text x="62" y="55" text-anchor="middle" :fill="boolValue ? alertColor : '#64748b'" font-size="7"
          font-weight="bold" font-family="monospace">
          {{ boolValue ? (numValue !== 0 ? Math.abs(numValue).toFixed(0) + (unit || 'Hz') : 'RUNNING') : 'STANDBY' }}
        </text>
      </g>

      <!-- Top Inverter Junction / Terminal Box (顶部变频接线盒) -->
      <g>
        <!-- Cable Gland Entry -->
        <rect x="57" y="2" width="10" height="5" rx="1.5" fill="#475569" stroke="#1e293b" stroke-width="0.75" />
        <line x1="59" y1="4" x2="65" y2="4" stroke="#94a3b8" stroke-width="1" />
        <!-- Box Body -->
        <rect x="48" y="6" width="28" height="14" rx="2.5" :fill="`url(#motor-tbox-${component.id})`" stroke="#1e293b"
          stroke-width="1" />
        <!-- Box Lid Bevel Line -->
        <line x1="50" y1="10" x2="74" y2="10" stroke="#64748b" stroke-width="0.75" />
        <!-- Fastener Screws -->
        <circle cx="51.5" cy="8" r="0.8" fill="#cbd5e1" />
        <circle cx="72.5" cy="8" r="0.8" fill="#cbd5e1" />

        <!-- Status Beacon / Run LED (双色高亮状态信源灯) -->
        <circle cx="69" cy="14" r="3" fill="#0f172a" stroke="#334155" stroke-width="0.75" />
        <circle cx="69" cy="14" r="2.2" :fill="boolValue ? alertColor : '#475569'" />
        <circle v-if="boolValue" cx="69" cy="14" r="1" fill="#ffffff" opacity="0.8" />

        <!-- High Voltage / Electric Symbol -->
        <path d="M 55 11 L 53 14 L 56 14 L 54 18 L 58 13.5 L 55.5 13.5 Z" fill="#eab308" />
      </g>

      <!-- Rear Fan Cowl & Dynamic High-Speed Cooling Fan (右侧导风罩与散热风扇) -->
      <g>
        <!-- Rear Cowl Housing (风罩外壳) -->
        <path d="M 90 22 L 115 26 L 115 72 L 90 76 Z" :fill="`url(#motor-cowl-${component.id})`" stroke="#1e293b"
          stroke-width="1" />

        <!-- Cowl Air Intake Slots -->
        <line x1="112" y1="32" x2="112" y2="66" stroke="#0f172a" stroke-width="2" stroke-linecap="round" />
        <line x1="108" y1="30" x2="108" y2="68" stroke="#0f172a" stroke-width="1.5" stroke-linecap="round"
          opacity="0.7" />

        <!-- Fan Housing Interior Window Aperture -->
        <circle cx="102" cy="49" r="14" fill="#090d16" stroke="#1e293b" stroke-width="1" />

        <!-- Dynamic High-Speed 6-Blade Cooling Fan (高速旋转叶片) -->
        <g :transform="`translate(102, 49) rotate(${motorAngle})`">
          <!-- 6 Curved Aerodynamic Blades -->
          <path d="M 0 0 C -2 -7 2 -12 0 -13 C -2 -12 -5 -7 0 0 Z" :fill="boolValue ? activeColor : '#64748b'" />
          <path d="M 0 0 C 6 -4 10 -6 11 -7 C 10 -9 5 -6 0 0 Z" :fill="boolValue ? activeColor : '#64748b'" />
          <path d="M 0 0 C 7 2 11 6 12 7 C 10 9 6 6 0 0 Z" :fill="boolValue ? activeColor : '#64748b'" />
          <path d="M 0 0 C 2 7 -2 12 0 13 C 2 12 5 7 0 0 Z" :fill="boolValue ? activeColor : '#64748b'" />
          <path d="M 0 0 C -6 4 -10 6 -11 7 C -10 9 -5 6 0 0 Z" :fill="boolValue ? activeColor : '#64748b'" />
          <path d="M 0 0 C -7 -2 -11 -6 -12 -7 C -10 -9 -6 -6 0 0 Z" :fill="boolValue ? activeColor : '#64748b'" />

          <!-- Center Hub Nose Cone -->
          <circle cx="0" cy="0" r="3.2" fill="#334155" stroke="#64748b" stroke-width="0.75" />
          <circle cx="0" cy="0" r="1.5" :fill="boolValue ? '#f8fafc' : '#94a3b8'" />
        </g>
      </g>
    </svg>

    <!-- 20. CUSTOM IMAGE（自定义图片图元） -->
    <div v-else-if="component.type === 'image'"
      class="w-full h-full flex items-center justify-center overflow-hidden select-none">
      <!-- tile：background-repeat 真平铺 -->
      <div v-if="(component.props.imageFit === 'tile') && (component.props.imageUrl || '').trim() && !imgError"
        :style="tileStyle" class="w-full h-full" />
      <!-- 常规 fit：img 渲染 -->
      <img v-else-if="(component.props.imageUrl || '').trim() && !imgError" :src="component.props.imageUrl" alt=""
        draggable="false" @error="imgError = true" class="pointer-events-none max-w-full max-h-full"
        :style="imageFitStyle" />
      <div v-else class="flex flex-col items-center gap-1 text-slate-400 dark:text-slate-500">
        <svg class="w-6 h-6" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
          stroke-linecap="round" stroke-linejoin="round">
          <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
          <circle cx="8.5" cy="8.5" r="1.5" />
          <polyline points="21 15 16 10 5 21" />
        </svg>
        <span class="text-[10px]">{{ imgError ? '图片加载失败' : '未设置图片' }}</span>
      </div>
    </div>

    <!-- 21. 大屏标题背景图元（title-header）5套风格 × 桌面/移动 -->
    <div v-else-if="component.type === 'title-header'" class="relative w-full h-full overflow-hidden select-none"
      :style="{
        background: headerTheme.background,
        color: headerTheme.text,
        border: headerTheme.border,
        borderRadius: headerTheme.borderRadius || '2px',
        backdropFilter: headerTheme.backdropFilter || 'none',
      }">
      <!-- 装饰 SVG：随画布等比拉伸，极简线条设计 -->
      <svg class="absolute inset-0 w-full h-full pointer-events-none" viewBox="0 0 100 100" preserveAspectRatio="none">
        <!-- 极简亮白风格：极致清爽，底部 1px 细分界线 -->
        <template v-if="headerStyle === 'pure-white'">
          <line x1="0" y1="99.5" x2="100" y2="99.5" stroke="#e2e8f0" stroke-width="0.8" />
          <line x1="0" y1="0.5" x2="100" y2="0.5" stroke="#f1f5f9" stroke-width="0.5" />
        </template>

        <!-- 工业钛灰风格：底部 2px 浅蓝装饰线条 -->
        <template v-else-if="headerStyle === 'titanium-light'">
          <line x1="0" y1="0.5" x2="100" y2="0.5" stroke="#e2e8f0" stroke-width="0.5" />
          <rect x="0" y="97.5" width="100" height="2.5" :fill="headerTheme.accent" opacity="0.85" />
        </template>

        <!-- 经典石板深灰风格：沉稳严谨，底部 1.5px 纯净细线 -->
        <template v-else-if="headerStyle === 'slate-dark'">
          <line x1="0" y1="0.7" x2="100" y2="0.7" :stroke="headerTheme.accent" stroke-width="0.5" opacity="0.25" />
          <rect x="0" y="98" width="100" height="2" :fill="headerTheme.accent" opacity="0.6" />
        </template>

        <!-- 悬浮通透胶囊风格：轻量微边框 -->
        <template v-else-if="headerStyle === 'translucent-frost'">
          <line x1="10" y1="99" x2="90" y2="99" :stroke="headerTheme.accent" stroke-width="0.6" opacity="0.3" />
        </template>

        <!-- 生态绿：菱形光带 + 中心能效光环 -->
        <template v-else-if="headerStyle === 'eco-green'">
          <line x1="0" y1="0.7" x2="100" y2="0.7" :stroke="headerTheme.accent" stroke-width="0.5" opacity="0.35" />
          <rect x="0" y="97" width="100" height="3" :fill="headerTheme.accent" opacity="0.5" />
          <rect x="8" y="32" width="10" height="10" :fill="headerTheme.accent" opacity="0.25"
            transform="rotate(45 13 37)" />
          <rect x="82" y="32" width="10" height="10" :fill="headerTheme.accent" opacity="0.25"
            transform="rotate(45 87 37)" />
          <line x1="0" y1="50" x2="100" y2="50" :stroke="headerTheme.accent" stroke-width="0.6" opacity="0.22"
            stroke-dasharray="2 3" />
        </template>

        <!-- 机能碳纤橙：斜纹装饰 -->
        <template v-else-if="headerStyle === 'carbon-orange'">
          <line x1="0" y1="0.7" x2="100" y2="0.7" :stroke="headerTheme.accent" stroke-width="0.5" opacity="0.35" />
          <rect x="0" y="97" width="100" height="3" :fill="headerTheme.accent" opacity="0.5" />
          <g :stroke="headerTheme.accent" stroke-width="1.1" opacity="0.16" stroke-linecap="round">
            <line x1="0" y1="108" x2="108" y2="0" />
            <line x1="12" y1="112" x2="112" y2="12" />
            <line x1="-12" y1="92" x2="92" y2="-12" />
          </g>
        </template>

        <!-- 深海商务暗蓝（默认 / tech-blue）：顶部微光 + 底部科技线条 -->
        <template v-else>
          <line x1="0" y1="0.7" x2="100" y2="0.7" :stroke="headerTheme.accent" stroke-width="0.5" opacity="0.35" />
          <rect x="0" y="97.5" width="100" height="2.5" :fill="headerTheme.accent" opacity="0.7" />
          <polygon :points="'0,12 18,0 28,0 0,28'" :fill="headerTheme.accentSoft" />
          <polygon :points="'100,88 82,100 72,100 100,72'" :fill="headerTheme.accentSoft" />
        </template>
      </svg>

      <!-- 桌面大屏布局：Logo｜主标题+副标题两行｜右端时钟+状态 -->
      <div v-if="headerDevice === 'desktop'"
        class="relative z-10 w-full h-full flex flex-col justify-center px-5 gap-0.5"
        :style="{ fontWeight: bold ? '700' : '600' }">
        <div class="flex items-center gap-3 min-w-0">
          <div class="shrink-0 flex items-center gap-1.5 border-2 rounded-md px-2.5 h-7"
            :style="{ color: headerTheme.accent, borderColor: headerTheme.accent, fontSize: `${fontSize}px` }">
            <span class="w-1.5 h-1.5 rounded-full" :style="{ background: headerTheme.accent }" />
            <span class="font-mono tracking-wider">{{ headerLogoText }}</span>
          </div>
          <span class="min-w-0 truncate" :style="{ fontSize: `${fontSize + 3}px`, color: headerTheme.text }">{{
            headerTitle
            }}</span>
          <div class="ml-auto shrink-0 flex items-center gap-3">
            <span v-if="headerShowClock" class="font-mono"
              :style="{ fontSize: `${fontSize}px`, color: headerTheme.accent }">{{ timeString }}</span>
            <span v-if="headerShowStatus" class="flex items-center gap-1.5 rounded-full px-2.5 h-6"
              :style="{ background: headerTheme.accentSoft, color: headerTheme.text, fontSize: `${Math.max(10, fontSize - 3)}px` }">
              <span class="w-1.5 h-1.5 rounded-full animate-pulse" :style="{ background: headerTheme.accent }" />
              {{ headerStatusText }}
            </span>
          </div>
        </div>
        <div v-if="headerSubtitle" class="truncate"
          :style="{ fontSize: `${Math.max(10, fontSize - 2)}px`, color: headerTheme.subText, letterSpacing: '0.1em' }">
          {{ headerSubtitle }}
        </div>
      </div>

      <!-- 移动竖屏布局：Logo｜标题｜右端时钟+状态点（紧凑单行） -->
      <div v-else class="relative z-10 w-full h-full flex items-center gap-2 px-2"
        :style="{ fontSize: `${fontSize}px`, fontWeight: bold ? '700' : '600' }">
        <span class="shrink-0 font-mono tracking-wide" :style="{ color: headerTheme.accent }">{{ headerLogoText
          }}</span>
        <span class="min-w-0 truncate" :style="{ color: headerTheme.text }">{{ headerTitle }}</span>
        <span v-if="headerShowClock" class="ml-auto shrink-0 font-mono"
          :style="{ fontSize: `${Math.max(10, fontSize - 2)}px`, color: headerTheme.accent }">{{ timeString }}</span>
        <span v-if="headerShowStatus" class="shrink-0 w-2 h-2 rounded-full animate-pulse"
          :style="{ background: headerTheme.accent }" :title="headerStatusText" />
      </div>
    </div>

    <!-- 22. 导航菜单图元（nav-menu）：桌面顶部横条 / 移动底部 Tab 栏 -->
    <div v-else-if="component.type === 'nav-menu'"
      class="relative w-full h-full overflow-hidden select-none flex items-stretch" :style="{
        background: navMenuTheme.background,
        border: navMenuTheme.border,
        backdropFilter: navMenuTheme.backdropFilter,
        WebkitBackdropFilter: navMenuTheme.backdropFilter,
      }">
      <!-- 顶部/底部流光刻线（与主题风格呼应） -->
      <div v-if="menuDevice === 'desktop'" class="absolute inset-x-0 bottom-0 h-[1.5px]"
        :style="{ background: navMenuTheme.accent, opacity: navMenuTheme.isLight ? 0.3 : 0.6 }" />
      <div v-else class="absolute inset-x-0 top-0 h-[1.5px]"
        :style="{ background: navMenuTheme.accent, opacity: navMenuTheme.isLight ? 0.3 : 0.6 }" />

      <!-- 桌面端：横向均分导航项（图标+文字水平排列，当前项底部高亮条） -->
      <div v-if="menuDevice === 'desktop'" class="relative z-10 flex w-full h-full">
        <div v-for="item in menuItems" :key="item.text + item.targetPageId"
          class="relative flex-1 flex items-center justify-center gap-2 h-full transition-colors duration-200"
          :class="isActiveMode && item.targetPageId ? (navMenuTheme.isLight ? 'cursor-pointer hover:bg-black/5' : 'cursor-pointer hover:bg-white/5') : ''"
          :data-nav-page="item.targetPageId || undefined" :style="{
            color: isCurrentMenuItem(item) ? navMenuTheme.activeText : navMenuTheme.itemText,
            fontWeight: isCurrentMenuItem(item) ? '600' : '500',
          }">
          <component :is="getMenuIcon(item.icon)" class="w-4 h-4 shrink-0"
            :style="{ color: isCurrentMenuItem(item) ? navMenuTheme.accent : navMenuTheme.itemText }" />
          <span class="truncate tracking-wide" :style="{
            fontSize: `${menuFontSize}px`,
            textShadow: isCurrentMenuItem(item) && !navMenuTheme.isLight ? `0 0 8px ${navMenuTheme.accent}` : 'none'
          }">
            {{ item.text }}
          </span>
          <!-- 当前项底部高亮条 -->
          <div v-if="isCurrentMenuItem(item)" class="absolute bottom-0 left-0 right-0 h-[3px]" :style="{
            background: navMenuTheme.accent,
            boxShadow: !navMenuTheme.isLight ? `0 0 10px ${navMenuTheme.accent}` : 'none'
          }" />
        </div>
      </div>

      <!-- 移动端：底部 Tab 栏（图标在上文字在下，当前项整体提亮） -->
      <div v-else class="relative z-10 flex w-full h-full">
        <div v-for="item in menuItems" :key="item.text + item.targetPageId"
          class="relative flex-1 flex flex-col items-center justify-center gap-0.5 h-full min-w-0 transition-colors duration-200"
          :class="isActiveMode && item.targetPageId ? (navMenuTheme.isLight ? 'cursor-pointer active:bg-black/5' : 'cursor-pointer active:bg-white/10') : ''"
          :data-nav-page="item.targetPageId || undefined" :style="{
            color: isCurrentMenuItem(item) ? navMenuTheme.activeText : navMenuTheme.itemText,
          }">
          <component :is="getMenuIcon(item.icon)" class="w-[18px] h-[18px] shrink-0" :style="{
            color: isCurrentMenuItem(item) ? navMenuTheme.accent : navMenuTheme.itemText,
            filter: isCurrentMenuItem(item) && !navMenuTheme.isLight ? `drop-shadow(0 0 6px ${navMenuTheme.accent})` : 'none',
          }" />
          <span class="truncate max-w-full px-0.5 leading-none"
            :style="{ fontSize: `${menuFontSize}px`, fontWeight: isCurrentMenuItem(item) ? '600' : '400' }">
            {{ item.text }}
          </span>
          <!-- 当前项顶部高亮条 -->
          <div v-if="isCurrentMenuItem(item)" class="absolute top-0 left-0 right-0 h-[3px]" :style="{
            background: navMenuTheme.accent,
            boxShadow: !navMenuTheme.isLight ? `0 0 10px ${navMenuTheme.accent}` : 'none'
          }" />
        </div>
      </div>
    </div>

    <!-- 23. 实时多变量监控看板（multi-var-dashboard） -->
    <div v-else-if="component.type === 'multi-var-dashboard'"
      class="w-full h-full flex flex-col relative overflow-hidden select-none transition-all duration-150"
      :style="dashboardContainerStyle">

      <!-- 标题栏（可选显示） -->
      <div v-if="showDashboardTitle"
        class="shrink-0 flex items-center justify-between px-3 py-1.5 border-b transition-colors" :style="{
          backgroundColor: dashboardTitleBgColor || 'rgba(241, 245, 249, 0.75)',
          borderColor: dashboardShowItemBorder ? dashboardItemBorderColor : 'rgba(226, 232, 240, 0.8)',
          color: dashboardTitleColor || '#1e293b'
        }">
        <div class="flex items-center gap-1.5 min-w-0">
          <div class="w-2 h-2 rounded-full bg-[#1890ff] shadow-sm shadow-sky-400/50" />
          <span class="text-xs font-bold tracking-wide truncate font-sans">
            {{ dashboardTitle }}
          </span>
        </div>
        <div class="flex items-center gap-1.5 shrink-0 text-[10px] font-mono opacity-75">
          <span class="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
          <span>{{ dashboardResolvedItems.length }} 点位</span>
        </div>
      </div>

      <!-- 看板主体内容区域 -->
      <div class="flex-1 p-2.5 overflow-y-auto overflow-x-hidden">
        <!-- 空状态提示 -->
        <div v-if="dashboardResolvedItems.length === 0"
          class="w-full h-full min-h-[80px] flex flex-col items-center justify-center text-slate-400 gap-1.5 text-center p-3">
          <LayoutDashboard class="w-6 h-6 stroke-1 text-slate-300 dark:text-slate-600" />
          <span class="text-xs">暂无监控变量点位</span>
          <span class="text-[10px] text-slate-400 dark:text-slate-500">请在右侧属性面板添加或一键导入变量</span>
        </div>

        <!-- 模式1：卡片网格 (grid) -->
        <div v-else-if="dashboardLayout === 'grid'" :style="dashboardGridStyle">
          <div v-for="item in dashboardResolvedItems" :key="item.id"
            class="flex flex-col justify-between p-2 rounded transition-all relative overflow-hidden" :style="{
              borderWidth: dashboardShowItemBorder ? '1px' : '0px',
              borderStyle: 'solid',
              borderColor: item.isAlarm ? item.statusColor : dashboardItemBorderColor,
              backgroundColor: item.isAlarm ? (item.isHigh ? 'rgba(239, 68, 68, 0.06)' : 'rgba(245, 158, 11, 0.06)') : (dashboardItemBgColor || '#f8fafc'),
              borderRadius: '6px',
            }">

            <!-- 卡片头部：标签与指示灯 -->
            <div class="flex items-center justify-between gap-1 mb-1">
              <div class="flex items-center gap-1 min-w-0 flex-1">
                <span v-if="item.showStatusDot" class="w-2 h-2 rounded-full shrink-0 transition-colors"
                  :class="item.isAlarm ? 'animate-pulse' : ''" :style="{ backgroundColor: item.statusColor }" />
                <span class="font-medium truncate leading-tight text-slate-700 dark:text-slate-200"
                  :style="{ fontSize: `${dashboardLabelFontSize}px` }" :title="`${item.label} (${item.variableKey})`">
                  {{ item.label }}
                </span>
              </div>
              <span v-if="item.isAlarm" class="text-[9px] px-1 py-0.2 rounded font-bold shrink-0 font-sans" :style="{
                backgroundColor: item.isHigh ? '#fee2e2' : '#fef3c7',
                color: item.isHigh ? '#dc2626' : '#d97706'
              }">
                {{ item.statusText }}
              </span>
            </div>

            <!-- 卡片数值主体 -->
            <div class="flex items-baseline justify-between gap-1 font-mono mt-0.5">
              <span class="font-bold tracking-tight tabular-nums truncate" :style="{
                fontSize: `${dashboardValueFontSize}px`,
                color: item.isAlarm ? item.statusColor : (item.isQualityBad ? '#94a3b8' : (activeColor || '#0f172a'))
              }">
                {{ item.displayVal }}
              </span>
              <span v-if="item.unit" class="text-[10px] text-slate-400 font-sans shrink-0 font-normal">
                {{ item.unit }}
              </span>
            </div>
          </div>
        </div>

        <!-- 模式2：列表表格 (table) -->
        <div v-else-if="dashboardLayout === 'table'" class="w-full">
          <table class="w-full text-left border-collapse text-xs">
            <thead>
              <tr class="border-b text-[10px] font-semibold text-slate-400"
                :style="{ borderColor: dashboardItemBorderColor }">
                <th class="py-1 px-1.5">变量/点位</th>
                <th class="py-1 px-1.5 text-right">实时数值</th>
                <th class="py-1 px-1.5 text-center">单位</th>
                <th class="py-1 px-1.5 text-center">状态</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(item, idx) in dashboardResolvedItems" :key="item.id" class="border-b transition-colors"
                :style="{
                  borderColor: dashboardItemBorderColor,
                  backgroundColor: dashboardZebra && idx % 2 === 1 ? 'rgba(0,0,0,0.02)' : 'transparent'
                }">
                <td class="py-1 px-1.5 truncate max-w-[120px]">
                  <div class="flex items-center gap-1">
                    <span v-if="item.showStatusDot" class="w-1.5 h-1.5 rounded-full shrink-0"
                      :style="{ backgroundColor: item.statusColor }" />
                    <span class="font-medium text-slate-700 dark:text-slate-200 truncate"
                      :style="{ fontSize: `${dashboardLabelFontSize}px` }" :title="item.label">
                      {{ item.label }}
                    </span>
                  </div>
                </td>
                <td class="py-1 px-1.5 text-right font-mono font-bold tabular-nums" :style="{
                  fontSize: `${dashboardValueFontSize}px`,
                  color: item.isAlarm ? item.statusColor : (item.isQualityBad ? '#94a3b8' : '#0f172a')
                }">
                  {{ item.displayVal }}
                </td>
                <td class="py-1 px-1.5 text-center text-[10px] text-slate-400 font-sans">
                  {{ item.unit || '-' }}
                </td>
                <td class="py-1 px-1.5 text-center">
                  <span class="text-[9px] px-1.5 py-0.5 rounded-full font-medium" :style="{
                    backgroundColor: item.isAlarm ? (item.isHigh ? '#fee2e2' : '#fef3c7') : '#dcfce7',
                    color: item.isAlarm ? (item.isHigh ? '#dc2626' : '#b45309') : '#15803d'
                  }">
                    {{ item.statusText }}
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- 模式3：紧凑标签 (compact) -->
        <div v-else-if="dashboardLayout === 'compact'" class="flex flex-wrap gap-1.5">
          <div v-for="item in dashboardResolvedItems" :key="item.id"
            class="flex items-center gap-1.5 px-2 py-1 rounded text-xs transition-all border" :style="{
              borderColor: item.isAlarm ? item.statusColor : dashboardItemBorderColor,
              backgroundColor: dashboardItemBgColor || '#f8fafc'
            }">
            <span v-if="item.showStatusDot" class="w-2 h-2 rounded-full shrink-0"
              :style="{ backgroundColor: item.statusColor }" />
            <span class="text-slate-600 dark:text-slate-300 font-medium"
              :style="{ fontSize: `${dashboardLabelFontSize}px` }">{{ item.label }}:</span>
            <span class="font-mono font-bold tabular-nums"
              :style="{ fontSize: `${dashboardValueFontSize}px`, color: item.statusColor }">{{ item.displayVal }}</span>
            <span v-if="item.unit" class="text-[10px] text-slate-400">{{ item.unit }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- ERROR -->
    <div v-else class="p-2 bg-slate-800 text-white rounded text-xs select-none">
      Unknown Widget: {{ component.type }}
    </div>

    <!-- 通用变量值浮签：showValue=true 且组件自身无内嵌数值时，在底部覆盖层显示当前值（#6） -->
    <div
      v-if="component.props.showValue && !['gauge-dial', 'gauge-level', 'digital-val', 'trend-chart', 'tank', 'sys-time', 'rounded-btn', 'button', 'image', 'text', 'title-header', 'nav-menu', 'multi-var-dashboard'].includes(component.type)"
      class="absolute inset-x-0 bottom-0 text-center text-[9px] font-mono bg-black/60 text-white rounded-b px-1 truncate pointer-events-none z-20 select-none">
      {{ typeof props.value === 'boolean' ? (boolValue ? onText : offText) : numValue.toFixed(1) + (unit ? ' ' + unit :
        '')
      }}
    </div>
  </div>
</template>

<style scoped>
@keyframes pulse {

  0%,
  100% {
    opacity: 1;
  }

  50% {
    opacity: 0.4;
  }
}
</style>
