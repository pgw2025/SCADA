<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted, watch } from 'vue';
import { HMIComponent } from '../types';

const props = defineProps<{
  component: HMIComponent;
  value: number | boolean;
  isActiveMode: boolean;
}>();

const numValue = computed(() => {
  return typeof props.value === 'number' ? props.value : props.value ? 100 : 0;
});

const boolValue = computed(() => {
  return typeof props.value === 'boolean' ? props.value : props.value > 0;
});

// Let's keep simple frame counter for animated widgets (conveyor boxes, flowing fluid, rotating pump)
const ticks = ref(0);
let animId: number | null = null;

const tick = () => {
  ticks.value = (ticks.value + 1) % 1000;
  animId = requestAnimationFrame(tick);
};

const startAnimation = () => {
  if (!animId) {
    animId = requestAnimationFrame(tick);
  }
};

const stopAnimation = () => {
  if (animId) {
    cancelAnimationFrame(animId);
    animId = null;
  }
};

onMounted(() => {
  if (props.isActiveMode) {
    startAnimation();
  }
});

watch(() => props.isActiveMode, (newVal) => {
  if (newVal) {
    startAnimation();
  } else {
    stopAnimation();
  }
});

onUnmounted(() => {
  stopAnimation();
});

// Extracted prop getters
const activeColor = computed(() => props.component.props.activeColor ?? '#10b981');
const inactiveColor = computed(() => props.component.props.inactiveColor ?? '#94a3b8');
const strokeColor = computed(() => props.component.props.strokeColor ?? '#475569');
const fillColor = computed(() => props.component.props.fillColor ?? '#cbd5e1');
const maxValue = computed(() => props.component.props.maxValue ?? 100);
const unit = computed(() => props.component.props.unit ?? '');
const thresholdMin = computed(() => props.component.props.thresholdMin ?? 0);
const thresholdMax = computed(() => props.component.props.thresholdMax ?? 100);
const fontSize = computed(() => props.component.props.fontSize ?? 12);
const align = computed(() => props.component.props.align ?? 'center');
const bold = computed(() => props.component.props.bold ?? false);

const isHighAlert = computed(() => numValue.value >= (thresholdMax.value ?? 90));
const isLowAlert = computed(() => numValue.value <= (thresholdMin.value ?? -1));
const alertColor = computed(() => isHighAlert.value ? '#ef4444' : isLowAlert.value ? '#f59e0b' : activeColor.value);

const width = computed(() => props.component.width);
const height = computed(() => props.component.height);

// Dynamic computed states for widget types:
// 1. Pump rotation angle
const pumpAngle = computed(() => boolValue.value ? (ticks.value * 24) % 360 : 0);

// 2. Valve handle angle
const valveAngle = computed(() => boolValue.value ? 0 : 90);

// 3. Tank fluid waves
const wavePath = computed(() => {
  const percentHeight = Math.min(100, Math.max(0, numValue.value));
  const fluidY = 10 + (100 - percentHeight);
  const waveOffset = props.isActiveMode ? (ticks.value * 0.15) % (2 * Math.PI) : 0;
  return `M 10 ${fluidY} Q 30 ${fluidY - 4 * Math.sin(waveOffset)}, 50 ${fluidY} T 90 ${fluidY} L 90 110 L 10 110 Z`;
});

// 4. Pipe Flow scroll offset for fluid simulation
const pipeScrollOffsetH = computed(() => numValue.value > 0 ? -(ticks.value * 2) % 30 : 0);
const pipeScrollOffsetV = computed(() => numValue.value > 0 ? (ticks.value * 2) % 30 : 0);

// 5. Dial Rotation Angle
const dialAngle = computed(() => {
  const minVal = 0;
  const maxVal = maxValue.value;
  const boundedVal = Math.max(minVal, Math.min(maxVal, numValue.value));
  const angleRange = 280;
  return -140 + ((boundedVal - minVal) / (maxVal - minVal)) * angleRange;
});

// 6. Trend chart path
const chartPath = computed(() => {
  const points: number[] = [];
  for (let i = 0; i < 15; i++) {
    const phase = (ticks.value + i * 4) * 0.1;
    const wave = Math.sin(phase) * 15 + Math.cos(phase * 0.5) * 8;
    const pt = Math.max(0, Math.min(100, numValue.value + wave));
    points.push(pt);
  }

  const padding = 10;
  const innerW = width.value - padding * 2;
  const innerH = height.value - padding * 2.5;

  let pathStr = '';
  const intervalX = innerW / (points.length - 1);
  points.forEach((val, index) => {
    const x = padding + index * intervalX;
    const y = padding + (innerH - (val / 100) * innerH);
    if (index === 0) {
      pathStr += `M ${x} ${y}`;
    } else {
      pathStr += ` L ${x} ${y}`;
    }
  });
  return pathStr;
});

// 7. Conveyor Speed steps
const conveyorBeltStep = computed(() => numValue.value > 0 ? (ticks.value * (numValue.value / 40)) % 40 : 0);

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

// 9. Multi-state translation computed property
const mappedStateText = computed(() => {
  const rawVal = props.value;
  const mappingsRaw = props.component.props.stateMappings;
  if (!mappingsRaw) {
    return String(rawVal);
  }
  
  try {
    const pairs = mappingsRaw.split(/[;；]/);
    for (const pair of pairs) {
      const idx = pair.indexOf(':');
      if (idx !== -1) {
        const key = pair.slice(0, idx).trim();
        const label = pair.slice(idx + 1).trim();
        if (key === String(rawVal)) {
          return label;
        }
      }
    }
  } catch (err) {
    console.error("State text mapping evaluation error", err);
  }
  return String(rawVal);
});
</script>

<template>
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
    <path
      v-if="boolValue"
      :d="`M 38 78 Q 42 55, 50 ${50 + (ticks % 3) * 2} Q 58 55, 62 78 Q 50 84, 38 78`"
      :fill="isHighAlert ? '#ef4444' : '#f97316'"
      :opacity="0.8 + Math.sin(ticks * 0.1) * 0.15"
    />
    <path
      v-if="boolValue"
      :d="`M 44 78 Q 46 62, 50 ${60 + (ticks % 2) * 2} Q 54 62, 56 78 Q 50 82, 44 78`"
      fill="#eab308"
      opacity="0.9"
    />
    
    <!-- Analog temperature indicator mini-bar -->
    <rect x="18" y="30" width="6" height="30" rx="3" fill="#1e293b" />
    <rect
      x="19"
      :y="30 + (30 - (Math.min(numValue, maxValue) / maxValue) * 30)"
      width="4"
      :height="(Math.min(numValue, maxValue) / maxValue) * 30"
      rx="2"
      :fill="alertColor"
    />
    
    <!-- Pressure release outlet -->
    <path d="M 85 40 L 95 40 L 95 48 M 90 40 L 90 35" stroke="#475569" stroke-width="2" fill="none" />
  </svg>

  <!-- 2. PUMP -->
  <svg v-else-if="component.type === 'pump'" width="100%" height="100%" viewBox="0 0 80 80" preserveAspectRatio="none">
    <!-- Pump Support Stand -->
    <rect x="10" y="65" width="60" height="10" fill="#334155" rx="2" />
    <rect x="25" y="55" width="30" height="10" fill="#475569" />
    
    <!-- Main circular casing -->
    <circle cx="40" cy="35" r="28" :fill="boolValue ? '#1e293b' : '#334155'" :stroke="boolValue ? alertColor : strokeColor" stroke-width="4" />
    
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
  <svg v-else-if="component.type === 'valve'" width="100%" height="100%" viewBox="0 0 80 80" preserveAspectRatio="none">
    <!-- Pipe Flanges -->
    <rect x="5" y="30" width="8" height="20" fill="#475569" />
    <rect x="67" y="30" width="8" height="20" fill="#475569" />
    
    <!-- Valves Body Triangles -->
    <polygon points="12,25 12,55 40,40" :fill="boolValue ? activeColor : inactiveColor" stroke="#334155" stroke-width="2" />
    <polygon points="68,25 68,55 40,40" :fill="boolValue ? activeColor : inactiveColor" stroke="#334155" stroke-width="2" />
    
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
    <text x="40" y="71" :fill="boolValue ? '#10b981' : '#f43f5e'" font-size="9" text-anchor="middle" font-weight="bold">
      {{ boolValue ? 'OPEN' : 'CLOSE' }}
    </text>
  </svg>

  <!-- 4. TANK -->
  <svg v-else-if="component.type === 'tank'" width="100%" height="100%" viewBox="0 0 100 120" preserveAspectRatio="none">
    <!-- Leg supports -->
    <line x1="20" y1="110" x2="15" y2="118" stroke="#475569" stroke-width="4" />
    <line x1="80" y1="110" x2="85" y2="118" stroke="#475569" stroke-width="4" />
    
    <!-- Main Glass Body container -->
    <rect x="8" y="8" width="84" height="104" rx="10" ry="10" fill="#1e293b" :stroke="strokeColor" stroke-width="3" />
    
    <!-- Wave flow surface -->
    <path
      v-if="numValue > 0"
      :d="numValue >= 99 ? 'M 10 10 L 90 10 L 90 110 L 10 110 Z' : wavePath"
      :fill="fillColor || '#3b82f6'"
      opacity="0.8"
    />
    
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
    
    <!-- Numeric Value Overlay -->
    <text x="50" y="65" text-anchor="middle" fill="#ffffff" font-size="11" font-weight="bold" stroke="#000" stroke-width="1" paint-order="stroke">
      {{ numValue.toFixed(1) }}%
    </text>
  </svg>

  <!-- 5. PIPE HORIZONTAL -->
  <div v-else-if="component.type === 'pipe-h'" class="w-full h-full relative flex items-center">
    <div
      class="absolute inset-x-0 h-4 rounded-full border-t border-b overflow-hidden shadow-inner flex items-center"
      :style="{
        backgroundColor: numValue > 0 ? '#334155' : inactiveColor,
        borderColor: strokeColor,
      }"
    >
      <div
        v-if="numValue > 0"
        class="w-[200%] h-1 opacity-70"
        :style="{
          backgroundImage: `repeating-linear-gradient(90deg, transparent, transparent 15px, ${activeColor} 15px, ${activeColor} 30px)`,
          transform: `translateX(${pipeScrollOffsetH}px)`,
        }"
      />
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
      }"
    >
      <div
        v-if="numValue > 0"
        class="h-[200%] w-1 opacity-70"
        :style="{
          backgroundImage: `repeating-linear-gradient(180deg, transparent, transparent 15px, ${activeColor} 15px, ${activeColor} 30px)`,
          transform: `translateY(${pipeScrollOffsetV}px)`,
        }"
      />
    </div>
    <div class="absolute top-0 left-0 right-0 h-2 bg-slate-700 rounded-sm border-b border-slate-500" />
    <div class="absolute bottom-0 left-0 right-0 h-2 bg-slate-700 rounded-sm border-t border-slate-500" />
  </div>

  <!-- 7. circular dial gauge -->
  <svg v-else-if="component.type === 'gauge-dial'" width="100%" height="100%" viewBox="0 0 100 100" preserveAspectRatio="contain">
    <circle cx="50" cy="50" r="46" fill="#1e293b" stroke="#334155" stroke-width="4" />
    <circle cx="50" cy="50" r="41" fill="#0f172a" />
    
    <!-- Warning Arc -->
    <path
      d="M 20 78 A 36 36 0 0 1 80 78"
      stroke="#10b981"
      stroke-width="4"
      fill="none"
      stroke-dasharray="100"
      stroke-dashoffset="24"
    />
    <path
      d="M 70 24 A 36 36 0 0 1 80 78"
      stroke="#f43f5e"
      stroke-width="4"
      fill="none"
    />
    
    <!-- Ticks -->
    <g stroke="#475569" stroke-width="2">
      <line x1="50" y1="12" x2="50" y2="16" />
      <line x1="88" y1="50" x2="84" y2="50" />
      <line x1="12" y1="50" x2="16" y2="50" />
      <line x1="23" y1="23" x2="26" y2="26" />
      <line x1="77" y1="23" x2="74" y2="26" />
    </g>
    
    <!-- Needle -->
    <g :transform="`translate(50, 50) rotate(${dialAngle})`">
      <path d="M -3 0 L 0 -38 L 3 0 Z" fill="#ef4444" />
      <circle cx="0" cy="0" r="5" fill="#f8fafc" />
    </g>
    
    <!-- Indicators Text -->
    <text x="50" y="70" text-anchor="middle" fill="#94a3b8" font-size="8" font-weight="medium">
      {{ component.label }}
    </text>
    <text x="50" y="84" text-anchor="middle" :fill="alertColor" font-size="11" font-weight="bold">
      {{ numValue.toFixed(1) }}
      <tspan font-size="8" fill="#64748b" dx="1">{{ unit || ' ' }}</tspan>
    </text>
  </svg>

  <!-- 8. LEVEL BAR -->
  <div v-else-if="component.type === 'gauge-level'" class="w-full h-full flex flex-col items-center bg-slate-900 border border-slate-700 rounded p-1 font-mono text-[9px] text-slate-400">
    <div class="flex-1 w-full bg-slate-950 border border-slate-800 rounded relative overflow-hidden flex flex-col justify-end">
      <div
        class="w-full transition-all duration-300"
        :style="{
          height: `${Math.min(100, Math.max(0, numValue))}%`,
          backgroundColor: alertColor,
          boxShadow: `0 0 12px ${alertColor}`,
        }"
      />
      <div class="absolute inset-0 flex flex-col justify-between p-1 opacity-50 pointer-events-none text-[8px] text-slate-300 font-mono">
        <span>H</span>
        <span>M</span>
        <span>L</span>
      </div>
    </div>
    <div class="mt-1 text-[10px] text-slate-100 font-bold truncate max-w-full">
      {{ numValue.toFixed(0) }}%
    </div>
  </div>

  <!-- 9. DIGITAL VALUE -->
  <div
    v-else-if="component.type === 'digital-val'"
    class="w-full h-full bg-slate-950 border-2 rounded-lg flex flex-col justify-center items-center px-2 py-1 shadow-inner relative overflow-hidden"
    :style="{ borderColor: isHighAlert ? '#ef4444' : '#1e293b' }"
  >
    <div class="absolute top-1 left-2 text-[8px] text-slate-400 uppercase tracking-widest font-mono">
      {{ component.label || 'MONITOR' }}
    </div>
    <div
      class="text-xl md:text-2xl font-black mt-2 font-mono tracking-widest"
      :style="{ color: isHighAlert ? '#ef4444' : '#34d399' }"
    >
      {{ typeof value === 'boolean' ? (boolValue ? 'ON / ACTIVE' : 'OFF / IDLE') : `${numValue.toFixed(2)}` }}
      <span v-if="typeof value !== 'boolean'" class="text-xs text-slate-500 font-normal ml-0.5">{{ unit || 'V' }}</span>
    </div>
    <div
      class="absolute bottom-1 right-2 w-1.5 h-1.5 rounded-full"
      :style="{
        backgroundColor: boolValue ? '#10b981' : '#ef4444',
        animation: boolValue ? 'pulse 1.2s infinite' : 'none',
      }"
    />
  </div>

  <!-- 10. REAL-TIME TREND CHART -->
  <div v-else-if="component.type === 'trend-chart'" class="w-full h-full bg-slate-950 border border-slate-800 rounded-lg p-2 font-mono text-[9px] text-slate-400 flex flex-col">
    <div class="flex justify-between items-center mb-1 text-[9px] border-b border-slate-800 pb-1">
      <span class="font-bold text-slate-300 truncate max-w-[70%]">{{ component.name || '核心参数趋势' }}</span>
      <span class="text-emerald-400 font-bold">{{ numValue.toFixed(1) }} {{ unit || '℃' }}</span>
    </div>
    <div class="flex-1 relative">
      <svg width="100%" height="100%">
        <line x1="0" y1="25%" x2="100%" y2="25%" stroke="#334155" stroke-width="0.5" stroke-dasharray="3" />
        <line x1="0" y1="50%" x2="100%" y2="50%" stroke="#334155" stroke-width="0.5" stroke-dasharray="3" />
        <line x1="0" y1="75%" x2="100%" y2="75%" stroke="#334155" stroke-width="0.5" stroke-dasharray="3" />
        <path
          :d="chartPath"
          fill="none"
          :stroke="isHighAlert ? '#ef4444' : '#10b981'"
          stroke-width="2.5"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </div>
  </div>

  <!-- 11. CONVEYOR BELT -->
  <svg v-else-if="component.type === 'conveyor'" width="100%" height="100%" viewBox="0 0 300 40" preserveAspectRatio="none">
    <rect x="5" y="12" width="290" height="16" rx="8" fill="#1e293b" :stroke="strokeColor" stroke-width="2.5" />
    <circle cx="15" cy="20" r="6" fill="#64748b" stroke="#334155" />
    <circle cx="15" cy="20" r="2" fill="#1e293b" />
    <circle cx="100" cy="20" r="6" fill="#64748b" stroke="#334155" />
    <circle cx="100" cy="20" r="2" fill="#1e293b" />
    <circle cx="200" cy="20" r="6" fill="#64748b" stroke="#334155" />
    <circle cx="200" cy="20" r="2" fill="#1e293b" />
    <circle cx="285" cy="20" r="6" fill="#64748b" stroke="#334155" />
    <circle cx="285" cy="20" r="2" fill="#1e293b" />
    
    <g v-if="numValue > 0">
      <rect :x="20 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
      <rect :x="100 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
      <rect :x="180 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
      <rect :x="260 + conveyorBeltStep" y="2" width="16" height="10" fill="#d97706" rx="1" />
    </g>
    <line x1="8" y1="31" x2="292" y2="31" stroke="#475569" stroke-width="2" stroke-dasharray="4 4" />
  </svg>

  <!-- 12. TEXT LABEL -->
  <div
    v-else-if="component.type === 'text'"
    class="w-full h-full flex items-center"
    :style="{
      justifyContent: align === 'center' ? 'center' : align === 'right' ? 'flex-end' : 'flex-start',
      fontSize: `${fontSize}px`,
      fontWeight: bold ? 'bold' : 'normal',
      color: activeColor || '#cbd5e1',
    }"
  >
    {{ component.label }}
  </div>

  <!-- 13. LED INDICATOR -->
  <div v-else-if="component.type === 'led'" class="w-full h-full flex flex-col items-center justify-center">
    <div
      class="rounded-full transition-all duration-300"
      :style="{
        width: `${Math.min(width, height) - 12}px`,
        height: `${Math.min(width, height) - 12}px`,
        backgroundColor: boolValue ? activeColor : inactiveColor,
        boxShadow: boolValue ? `0 0 16px ${activeColor}, inset 0 2px 4px rgba(255,255,255,0.4)` : 'inset 0 2px 4px rgba(0,0,0,0.4)',
        border: '3px solid #334155',
      }"
    />
    <span class="text-[9px] text-slate-300 font-mono mt-1 text-center truncate max-w-full">
      {{ component.label }}
    </span>
  </div>

  <!-- 14. INDUSTRIAL BUTTON -->
  <div
    v-else-if="component.type === 'button'"
    class="w-full h-full p-0.5"
  >
    <div
      class="w-full h-full rounded border-2 shadow flex flex-col items-center justify-center transition-all select-none relative overflow-hidden"
      :class="[
        isActiveMode ? 'active:translate-y-0.5 active:shadow-inner cursor-pointer' : '',
        boolValue ? 'shadow-inner' : 'shadow-md border-t-white border-l-white border-b-slate-900 border-r-slate-900'
      ]"
      :style="{
        backgroundColor: boolValue ? activeColor : fillColor || '#cbd5e1',
        borderColor: boolValue ? '#0284c7' : '#94a3b8',
        color: boolValue ? '#ffffff' : '#1e293b'
      }"
    >
      <!-- Led Indicator inside the button -->
      <div 
        class="absolute top-1 right-2 w-1.5 h-1.5 rounded-full border border-slate-600/30"
        :style="{
          backgroundColor: boolValue ? '#22c55e' : '#dc2626',
          boxShadow: boolValue ? '0 0 6px #22c55e' : 'none'
        }"
      />
      <!-- Label -->
      <span 
        class="text-center font-mono pointer-events-none px-1 truncate max-w-full"
        :style="{
          fontSize: `${fontSize}px`,
          fontWeight: bold ? 'bold' : 'normal'
        }"
      >
        {{ component.props.buttonText || component.label || '命令键' }}
      </span>
      <span class="text-[8px] opacity-60 font-sans pointer-events-none mt-0.5 select-none" v-if="component.bindField">
        {{ component.props.buttonMode === 'momentary' ? '[点动]' : component.props.buttonMode === 'set-value' ? `[设值:${component.props.clickValue ?? 0}]` : '[自锁]' }}
      </span>
    </div>
  </div>

  <!-- 15. TOGGLE SWITCH -->
  <div v-else-if="component.type === 'switch'" class="w-full h-full flex flex-col items-center justify-center p-1 font-mono text-[9px] select-none">
    <div class="w-full h-full bg-[#1e293b] border border-slate-700 rounded p-1.5 flex flex-col items-center justify-between shadow-md">
      <!-- Top State Label -->
      <span class="text-slate-400 font-bold uppercase text-[8px] tracking-tight text-center truncate max-w-full">
        {{ boolValue ? 'RUN / 开启' : 'STOP / 二位' }}
      </span>
      
      <!-- Slot slider & Lever knob style-->
      <div 
        class="w-6 h-10 bg-slate-950 rounded-full border border-slate-800 relative flex items-center justify-center overflow-hidden shadow-inner cursor-pointer"
      >
        <div 
          class="w-5 h-5 rounded-full bg-slate-300 border border-slate-500 shadow-md transition-all duration-300 flex items-center justify-center"
          :style="{
            transform: boolValue ? 'translateY(-10px)' : 'translateY(10px)',
            backgroundColor: boolValue ? '#10b981' : '#ef4444',
            boxShadow: boolValue ? '0 2px 4px rgba(16,185,129,0.4)' : '0 2px 4px rgba(239,68,68,0.4)',
          }"
        >
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
  <div
    v-else-if="component.type === 'sys-time'"
    class="w-full h-full bg-black/90 border-2 border-slate-800 rounded-lg flex flex-col justify-center items-center px-2 py-1 shadow-inner relative text-emerald-400 font-mono select-none"
  >
    <div class="absolute top-1 left-2 text-[8px] text-slate-500 uppercase tracking-widest leading-none">
      {{ component.label || 'SYSTEM CLOCK' }}
    </div>
    <!-- Interactive time ticking -->
    <div 
      class="text-[13px] sm:text-[14px] font-bold text-center mt-2.5 tracking-wider w-full truncate"
      v-text="timeString"
    />
    <div class="absolute bottom-1 right-2 flex items-center gap-1">
      <span class="w-1 h-1 rounded-full bg-emerald-500 animate-pulse" />
      <span class="text-[7px] text-slate-500">LIVE</span>
    </div>
  </div>

  <!-- 17. MULTI-STATE TRANSLATION TEXT -->
  <div
    v-else-if="component.type === 'state-text'"
    class="w-full h-full bg-slate-900 border border-slate-700/80 rounded px-2 py-1 flex flex-col justify-center font-mono relative overflow-hidden select-none"
  >
    <span class="text-[8px] text-slate-500 uppercase tracking-widest absolute top-1 left-2">
      {{ component.label || '状态监测器' }}
    </span>
    <div class="mt-2 text-xs font-bold flex items-center justify-between">
      <span 
        class="text-sky-400 truncate flex-1"
        :style="{
          fontSize: `${fontSize}px`,
          fontWeight: bold ? 'bold' : 'normal'
        }"
      >
        {{ mappedStateText }}
      </span>
      <span class="text-[8px] text-slate-500 bg-slate-950 px-1 py-0.5 rounded leading-none border border-slate-800 ml-1">
        VAL:{{ value }}
      </span>
    </div>
  </div>

  <!-- 18. INDUSTRIAL AC MOTOR -->
  <svg v-else-if="component.type === 'motor'" width="100%" height="100%" viewBox="0 0 100 80" preserveAspectRatio="none">
    <!-- Motor Mounting base feet -->
    <rect x="15" y="66" width="70" height="6" fill="#334155" rx="1" />
    <rect x="25" y="58" width="50" height="8" fill="#475569" />
    
    <!-- Output drive shaft (left of the motor) -->
    <rect x="2" y="32" width="15" height="10" fill="#94a3b8" />
    <line x1="2" y1="37" x2="17" y2="37" stroke="#cbd5e1" stroke-width="1.5" />
    
    <!-- Main cylindrical body -->
    <rect x="24" y="14" width="52" height="46" rx="4" :fill="boolValue ? '#1e293b' : '#334155'" :stroke="boolValue ? alertColor : strokeColor" stroke-width="3" />
    
    <!-- Ribbed stator shell / Cooling Fins (Horizontal stripes for nice depth) -->
    <line x1="30" y1="22" x2="70" y2="22" stroke="#475569" stroke-width="2" />
    <line x1="30" y1="28" x2="70" y2="28" stroke="#475569" stroke-width="2" />
    <line x1="30" y1="34" x2="70" y2="34" stroke="#475569" stroke-width="2" />
    <line x1="30" y1="40" x2="70" y2="40" stroke="#475569" stroke-width="2" />
    <line x1="30" y1="46" x2="70" y2="46" stroke="#475569" stroke-width="2" />
    <line x1="30" y1="52" x2="70" y2="52" stroke="#475569" stroke-width="2" />
    
    <!-- Electrical Junction/Terminal Box (Upper piece) -->
    <rect x="42" y="4" width="16" height="12" fill="#475569" rx="1" stroke="#334155" stroke-width="1" />
    <circle cx="50" cy="10" r="2.5" fill="#eab308" />
    
    <!-- Fan Cowl / Protective Back Fan housing (right side) -->
    <path d="M 76 17 L 90 22 L 90 52 L 76 57 Z" fill="#1e293b" stroke="#334155" stroke-width="1.5" />
    
    <!-- Fast spinning cooling fan blades inside the cowl representation -->
    <g :transform="`translate(83, 37) rotate(${pumpAngle})`">
      <circle cx="0" cy="0" r="1.5" fill="#94a3b8" />
      <polygon points="-2,-13 2,-13 0,0" :fill="boolValue ? activeColor : '#64748b'" />
      <polygon points="-2,13 2,13 0,0" :fill="boolValue ? activeColor : '#64748b'" />
      <polygon points="-13,-2 -13,2 0,0" :fill="boolValue ? activeColor : '#64748b'" />
      <polygon points="13,-2 13,2 0,0" :fill="boolValue ? activeColor : '#64748b'" />
    </g>
    
    <!-- Run indicator led -->
    <circle cx="34" cy="50" r="3.5" :fill="boolValue ? '#10b981' : '#dc2626'" />
  </svg>

  <!-- ERROR -->
  <div v-else class="p-2 bg-slate-800 text-white rounded text-xs select-none">
    Unknown Widget: {{ component.type }}
  </div>
</template>

<style scoped>
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}
</style>
