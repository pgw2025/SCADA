// 共享 composable：HMI 图元通用逻辑的唯一真相源。
// 所有 widgets/*.vue 子组件均从此处取用 props 契约与通用派生计算，
// 避免与 InspectorPanel / widgetRegistry 出现属性 fallback 双轨不一致。
import { computed, ref, onMounted, onUnmounted, watch } from 'vue';
import type { HMIComponent } from '../../types';
import { getWidgetDef } from '../../widgetRegistry';
import { ticks, subscribeAnimation, unsubscribeAnimation } from '../../utils/animationTicker';
import type { TrendSample } from '../../utils/trendHistory';

export interface HmiWidgetProps {
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
}

export function useWidgetBase(props: HmiWidgetProps) {
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

  // 阶段5-6：text 解耦——开关/阀/数显等有状态文本控件，状态文案改为 props 可配置，默认中文
  const onText = computed(() => props.component.props.onText || '开启');
  const offText = computed(() => props.component.props.offText || '关闭');

  // 变量质量非 Good 时显示 -- 而非旧值（配合 CanvasPanel 质量角标）
  const qualityBad = computed(() => !!props.quality && props.quality !== 'Good');

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

  // 通用外观尺寸（led 等需要组件自身宽高做圆形直径）
  const width = computed(() => props.component.width);
  const height = computed(() => props.component.height);

  // 自动走时：sys-time 与 title-header 时钟共用（原 HMIWidget 每实例均订阅，此处保持一致）
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

  return {
    isLockedControl,
    numValue,
    boolValue,
    normalizedPercent,
    defDefaults,
    propOr,
    activeColor,
    inactiveColor,
    strokeColor,
    fillColor,
    minValue,
    maxValue,
    unit,
    thresholdMin,
    thresholdMax,
    fontSize,
    align,
    bold,
    showBorder,
    showBackground,
    showInnerLabel,
    onText,
    offText,
    qualityBad,
    hasExplicitThresholdMax,
    hasExplicitThresholdMin,
    isHighAlert,
    isLowAlert,
    alertColor,
    width,
    height,
    ticks,
    timeString,
  };
}
