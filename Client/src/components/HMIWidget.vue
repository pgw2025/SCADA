<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted, watch } from 'vue';
import { HMIComponent } from '../types';
import { getWidgetDef } from '../widgetRegistry';
// 全局共享动画时钟：单实例 rAF 驱动所有动画器件，避免每组件独立 rAF（见 #13）
import { ticks, subscribeAnimation, unsubscribeAnimation } from '../utils/animationTicker';

const props = defineProps<{
  component: HMIComponent;
  value: number | boolean;
  isActiveMode: boolean;
  controlLocked?: boolean;
  /** 趋势图真实数据窗口（父级维护的滚动缓冲，无则显示占位） */
  history?: number[];
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
const thresholdMin = computed(() => Number(propOr('thresholdMin', 10)));
const thresholdMax = computed(() => Number(propOr('thresholdMax', 90)));
const fontSize = computed(() => Number(propOr('fontSize', 12)));
const align = computed<'left' | 'center' | 'right'>(() =>
  (propOr('align', 'center') as 'left' | 'center' | 'right') || 'center');
const bold = computed(() => propOr('bold', false));

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

// 阶段5-6：趋势图过渡占位——未绑定设备/变量（无数据源）时不绘制伪造曲线，展示占位提示
const hasTrendData = computed(() =>
  props.component.bindDeviceId != null && !!props.component.bindVariableKey
);
// 是否有 ≥2 个真实采样点可绘制（未绑定或刚绑定待采样时显示占位）
const trendReady = computed(() => (props.history?.length ?? 0) >= 2);

const isHighAlert = computed(() => numValue.value >= thresholdMax.value);
const isLowAlert = computed(() => numValue.value <= thresholdMin.value);
const alertColor = computed(() => isHighAlert.value ? '#ef4444' : isLowAlert.value ? '#f59e0b' : activeColor.value);

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

// 6. Trend chart path —— 基于 history 真实数据窗口 + 量程归一化（替代伪造正弦波）
const chartPath = computed(() => {
  const pts = props.history ?? [];
  if (pts.length < 2) return '';

  const padding = 10;
  const innerW = width.value - padding * 2;
  const innerH = height.value - padding * 2.5;
  if (innerW <= 0 || innerH <= 0) return '';

  // Y 轴量程：优先配置量程（minValue/maxValue），未配置时按数据自适应（±10% 余量）
  const lo = minValue.value, hi = maxValue.value;
  let yMin: number, yMax: number;
  if (Number.isFinite(lo) && Number.isFinite(hi) && hi > lo) {
    yMin = lo; yMax = hi;
  } else {
    const dMin = Math.min(...pts), dMax = Math.max(...pts);
    const margin = (dMax - dMin) * 0.1 || 1;
    yMin = dMin - margin; yMax = dMax + margin;
  }

  // 将采样窗口压缩到可视宽度（约每 6px 一个点），窗口过大时降采样
  const window = pts.slice(-Math.max(2, Math.floor(innerW / 6)));
  const intervalX = innerW / (window.length - 1);

  let pathStr = '';
  window.forEach((val, index) => {
    const x = padding + index * intervalX;
    const ratio = Math.max(0, Math.min(1, (val - yMin) / (yMax - yMin)));
    const y = padding + (innerH - ratio * innerH);
    pathStr += `${index === 0 ? 'M' : ' L'} ${x.toFixed(1)} ${y.toFixed(1)}`;
  });
  return pathStr;
});

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

    <!-- 10. REAL-TIME TREND CHART -->
    <div v-else-if="component.type === 'trend-chart'"
      class="w-full h-full bg-slate-950 border border-slate-800 rounded-lg p-2 font-mono text-[9px] text-slate-400 flex flex-col">
      <div class="flex justify-between items-center mb-1 text-[9px] border-b border-slate-800 pb-1">
        <span class="font-bold text-slate-300 truncate max-w-[70%]">{{ component.label || component.name || '实时趋势'
        }}</span>
        <span v-if="hasTrendData" class="text-emerald-400 font-bold">
          {{ numValue.toFixed(1) }}<template v-if="unit"> {{ unit }}</template>
        </span>
        <span v-else class="text-slate-500">--</span>
      </div>
      <!-- 阶段5-6：趋势图占位——未绑定数据源或采样点不足时不绘制伪造曲线 -->
      <div v-if="!trendReady" class="flex-1 flex flex-col items-center justify-center gap-1 text-slate-500">
        <span class="w-1.5 h-1.5 rounded-full bg-slate-600 animate-pulse" />
        <span class="text-[9px]">{{ hasTrendData ? '等待采样…' : '暂无数据' }}</span>
        <span class="text-[8px] text-slate-600">{{ hasTrendData ? '采集 ≥2 点后自动绘制' : '请在编辑器中绑定变量' }}</span>
      </div>
      <div v-else class="flex-1 relative">
        <svg width="100%" height="100%">
          <line x1="0" y1="25%" x2="100%" y2="25%" stroke="#334155" stroke-width="0.5" stroke-dasharray="3" />
          <line x1="0" y1="50%" x2="100%" y2="50%" stroke="#334155" stroke-width="0.5" stroke-dasharray="3" />
          <line x1="0" y1="75%" x2="100%" y2="75%" stroke="#334155" stroke-width="0.5" stroke-dasharray="3" />
          <path :d="chartPath" fill="none" :stroke="isHighAlert ? '#ef4444' : '#10b981'" stroke-width="2.5"
            stroke-linecap="round" stroke-linejoin="round" />
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

        <!-- Mode badge / hint -->
        <span class="text-[8px] opacity-75 font-sans pointer-events-none mt-0.5 select-none z-10">
          {{ component.props.buttonMode === 'momentary' ? '[按1送0]' : component.props.buttonMode === 'set-bit' ? '[置位1]'
            :
            component.props.buttonMode === 'reset-bit' ? '[复位0]' : component.props.buttonMode === 'set-value' ?
              `[设值:${component.props.clickValue ?? 0}]` : component.props.buttonMode === 'navigate' ? '[跳转]' : '[取反]' }}
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

    <!-- ERROR -->
    <div v-else class="p-2 bg-slate-800 text-white rounded text-xs select-none">
      Unknown Widget: {{ component.type }}
    </div>

    <!-- 通用变量值浮签：showValue=true 且组件自身无内嵌数值时，在底部覆盖层显示当前值（#6） -->
    <div
      v-if="component.props.showValue && !['gauge-dial', 'gauge-level', 'digital-val', 'trend-chart', 'tank', 'sys-time', 'rounded-btn', 'button', 'image', 'text'].includes(component.type)"
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
