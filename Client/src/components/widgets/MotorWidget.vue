<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const motorAngle = computed(() =>
  boolValue.value ? (ticks.value * (16 + Math.min(48, Math.abs(numValue.value) / 3))) % 360 : 0
);
</script>

<template>
<svg width="100%" height="100%" viewBox="0 0 120 90"
      preserveAspectRatio="xMidYMid meet" class="select-none overflow-visible">
      <defs>
        <!-- Stator Cylindrical Metal Gradient -->
        <linearGradient :id="'motor-stator-' + component.id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="var(--vfd-metal-700)" />
          <stop offset="18%" stop-color="var(--vfd-metal-600)" />
          <stop offset="50%" stop-color="var(--vfd-metal-800)" />
          <stop offset="85%" stop-color="var(--vfd-metal-900)" />
          <stop offset="100%" stop-color="var(--vfd-metal-800)" />
        </linearGradient>

        <!-- Front / Rear Flange Gradient -->
        <linearGradient :id="'motor-flange-' + component.id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="var(--vfd-metal-500)" />
          <stop offset="30%" stop-color="var(--vfd-metal-400)" />
          <stop offset="70%" stop-color="var(--vfd-metal-700)" />
          <stop offset="100%" stop-color="var(--vfd-metal-800)" />
        </linearGradient>

        <!-- Stainless Steel Shaft Gradient -->
        <linearGradient :id="'motor-shaft-' + component.id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="var(--vfd-metal-500)" />
          <stop offset="25%" stop-color="var(--vfd-metal-300)" />
          <stop offset="45%" stop-color="var(--vfd-metal-100)" />
          <stop offset="75%" stop-color="var(--vfd-metal-400)" />
          <stop offset="100%" stop-color="var(--vfd-metal-600)" />
        </linearGradient>

        <!-- Rear Fan Cowl Gradient -->
        <linearGradient :id="'motor-cowl-' + component.id" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="var(--vfd-metal-800)" />
          <stop offset="60%" stop-color="var(--vfd-metal-700)" />
          <stop offset="100%" stop-color="var(--vfd-metal-800)" />
        </linearGradient>

        <!-- Terminal Box Gradient -->
        <linearGradient :id="'motor-tbox-' + component.id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="var(--vfd-metal-600)" />
          <stop offset="40%" stop-color="var(--vfd-metal-700)" />
          <stop offset="100%" stop-color="var(--vfd-metal-800)" />
        </linearGradient>
      </defs>

      <!-- Base / Footings (Cast Iron Machine Feet) -->
      <g>
        <!-- Anti-vibration damper line -->
        <rect x="22" y="80" width="76" height="3" rx="1" fill="var(--vfd-metal-900)" opacity="0.8" />
        <!-- Left Foot -->
        <path d="M 28 66 L 24 79 L 46 79 L 43 66 Z" fill="var(--vfd-metal-700)" stroke="var(--vfd-metal-800)" stroke-width="1" />
        <rect x="26" y="74" width="18" height="5.5" rx="1.5" fill="var(--vfd-metal-800)" />
        <!-- Bolt Hole & Hex Bolt Left -->
        <circle cx="35" cy="76.5" r="2.5" fill="var(--vfd-metal-900)" />
        <circle cx="35" cy="76.5" r="1.5" fill="var(--vfd-metal-400)" />

        <!-- Right Foot -->
        <path d="M 77 66 L 74 79 L 96 79 L 92 66 Z" fill="var(--vfd-metal-700)" stroke="var(--vfd-metal-800)" stroke-width="1" />
        <rect x="76" y="74" width="18" height="5.5" rx="1.5" fill="var(--vfd-metal-800)" />
        <!-- Bolt Hole & Hex Bolt Right -->
        <circle cx="85" cy="76.5" r="2.5" fill="var(--vfd-metal-900)" />
        <circle cx="85" cy="76.5" r="1.5" fill="var(--vfd-metal-400)" />

        <!-- Machine Base Connecting Bar -->
        <rect x="25" y="66" width="70" height="5" fill="var(--vfd-metal-800)" stroke="var(--vfd-metal-700)" stroke-width="0.75" />
      </g>

      <!-- Drive Shaft & Output Flange (Left) -->
      <g>
        <!-- Main Output Shaft -->
        <rect x="4" y="42" width="22" height="14" rx="1.5" :fill="`url(#motor-shaft-${component.id})`" stroke="var(--vfd-metal-600)"
          stroke-width="0.75" />
        <line x1="4" y1="46" x2="26" y2="46" stroke="var(--vfd-metal-100)" stroke-width="1" opacity="0.6" />

        <!-- Keyway & Shaft Rotation dynamic tick (Spinning when running) -->
        <rect x="7" y="44.5" width="10" height="3" rx="0.5" fill="var(--vfd-metal-700)" opacity="0.7" />
        <g :transform="`translate(9, 49) rotate(${motorAngle})`">
          <circle cx="0" cy="0" r="3" fill="var(--vfd-metal-900)" opacity="0.4" />
          <line x1="-3" y1="0" x2="3" y2="0" :stroke="boolValue ? activeColor : 'var(--vfd-metal-400)'" stroke-width="1.5"
            stroke-linecap="round" />
        </g>

        <!-- Shaft Coupling Collar / Step -->
        <rect x="20" y="39" width="6" height="20" rx="1" :fill="`url(#motor-flange-${component.id})`" stroke="var(--vfd-metal-800)"
          stroke-width="0.75" />

        <!-- Front Drive End-Shield / Flange (前轴承盖法兰) -->
        <rect x="26" y="24" width="8" height="50" rx="2" :fill="`url(#motor-flange-${component.id})`" stroke="var(--vfd-metal-800)"
          stroke-width="1" />
        <!-- Flange Mounting Hex Bolts -->
        <circle cx="30" cy="28" r="1.5" fill="var(--vfd-metal-300)" stroke="var(--vfd-metal-700)" stroke-width="0.5" />
        <circle cx="30" cy="38" r="1.5" fill="var(--vfd-metal-300)" stroke="var(--vfd-metal-700)" stroke-width="0.5" />
        <circle cx="30" cy="60" r="1.5" fill="var(--vfd-metal-300)" stroke="var(--vfd-metal-700)" stroke-width="0.5" />
        <circle cx="30" cy="70" r="1.5" fill="var(--vfd-metal-300)" stroke="var(--vfd-metal-700)" stroke-width="0.5" />
      </g>

      <!-- Stator Housing (Main Body) & Aluminum Cooling Ribs -->
      <g>
        <!-- Stator Barrel Core -->
        <rect x="34" y="19" width="56" height="60" rx="3" :fill="`url(#motor-stator-${component.id})`"
          :stroke="boolValue ? alertColor : 'var(--vfd-metal-700)'" :stroke-width="boolValue ? 1.5 : 1" />

        <!-- Running Electromagnetic Field Aura / Active Glow -->
        <rect v-if="boolValue" x="33" y="18" width="58" height="62" rx="4" fill="none" :stroke="alertColor"
          stroke-width="1" opacity="0.4" />

        <!-- 7 Precision Cooling Fins (散热肋片) with light/shadow edges -->
        <g stroke-linecap="round">
          <!-- Fin 1 -->
          <line x1="35" y1="23" x2="89" y2="23" stroke="var(--vfd-metal-600)" stroke-width="2" />
          <line x1="35" y1="24.5" x2="89" y2="24.5" stroke="var(--vfd-metal-900)" stroke-width="1" />
          <!-- Fin 2 -->
          <line x1="35" y1="29" x2="89" y2="29" stroke="var(--vfd-metal-600)" stroke-width="2" />
          <line x1="35" y1="30.5" x2="89" y2="30.5" stroke="var(--vfd-metal-900)" stroke-width="1" />
          <!-- Fin 3 -->
          <line x1="35" y1="35" x2="89" y2="35" stroke="var(--vfd-metal-600)" stroke-width="2" />
          <line x1="35" y1="36.5" x2="89" y2="36.5" stroke="var(--vfd-metal-900)" stroke-width="1" />
          <!-- Fin 4 -->
          <line x1="35" y1="62" x2="89" y2="62" stroke="var(--vfd-metal-600)" stroke-width="2" />
          <line x1="35" y1="63.5" x2="89" y2="63.5" stroke="var(--vfd-metal-900)" stroke-width="1" />
          <!-- Fin 5 -->
          <line x1="35" y1="68" x2="89" y2="68" stroke="var(--vfd-metal-600)" stroke-width="2" />
          <line x1="35" y1="69.5" x2="89" y2="69.5" stroke="var(--vfd-metal-900)" stroke-width="1" />
          <!-- Fin 6 -->
          <line x1="35" y1="74" x2="89" y2="74" stroke="var(--vfd-metal-600)" stroke-width="2" />
          <line x1="35" y1="75.5" x2="89" y2="75.5" stroke="var(--vfd-metal-900)" stroke-width="1" />
        </g>

        <!-- Center Nameplate Badge (工业铭牌面板) -->
        <rect x="42" y="39" width="40" height="20" rx="2" fill="var(--vfd-metal-900)" stroke="var(--vfd-metal-700)" stroke-width="1" />
        <!-- Label / Title -->
        <text x="62" y="46" text-anchor="middle" fill="var(--vfd-metal-400)" font-size="5.5" font-weight="600"
          font-family="sans-serif">
          {{ component.label || 'AC SERVO' }}
        </text>
        <!-- Dynamic Speed / State Readout -->
        <text x="62" y="55" text-anchor="middle" :fill="boolValue ? alertColor : 'var(--vfd-metal-500)'" font-size="7"
          font-weight="bold" font-family="monospace">
          {{ boolValue ? (numValue !== 0 ? Math.abs(numValue).toFixed(0) + (unit || 'Hz') : 'RUNNING') : 'STANDBY' }}
        </text>
      </g>

      <!-- Top Inverter Junction / Terminal Box (顶部变频接线盒) -->
      <g>
        <!-- Cable Gland Entry -->
        <rect x="57" y="2" width="10" height="5" rx="1.5" fill="var(--vfd-metal-600)" stroke="var(--vfd-metal-800)" stroke-width="0.75" />
        <line x1="59" y1="4" x2="65" y2="4" stroke="var(--vfd-metal-400)" stroke-width="1" />
        <!-- Box Body -->
        <rect x="48" y="6" width="28" height="14" rx="2.5" :fill="`url(#motor-tbox-${component.id})`" stroke="var(--vfd-metal-800)"
          stroke-width="1" />
        <!-- Box Lid Bevel Line -->
        <line x1="50" y1="10" x2="74" y2="10" stroke="var(--vfd-metal-500)" stroke-width="0.75" />
        <!-- Fastener Screws -->
        <circle cx="51.5" cy="8" r="0.8" fill="var(--vfd-metal-300)" />
        <circle cx="72.5" cy="8" r="0.8" fill="var(--vfd-metal-300)" />

        <!-- Status Beacon / Run LED (双色高亮状态信源灯) -->
        <circle cx="69" cy="14" r="3" fill="var(--vfd-metal-900)" stroke="var(--vfd-metal-700)" stroke-width="0.75" />
        <circle cx="69" cy="14" r="2.2" :fill="boolValue ? alertColor : 'var(--vfd-metal-600)'" />
        <circle v-if="boolValue" cx="69" cy="14" r="1" fill="var(--vfd-metal-100)" opacity="0.8" />

        <!-- High Voltage / Electric Symbol -->
        <path d="M 55 11 L 53 14 L 56 14 L 54 18 L 58 13.5 L 55.5 13.5 Z" fill="#eab308" />
      </g>

      <!-- Rear Fan Cowl & Dynamic High-Speed Cooling Fan (右侧导风罩与散热风扇) -->
      <g>
        <!-- Rear Cowl Housing (风罩外壳) -->
        <path d="M 90 22 L 115 26 L 115 72 L 90 76 Z" :fill="`url(#motor-cowl-${component.id})`" stroke="var(--vfd-metal-800)"
          stroke-width="1" />

        <!-- Cowl Air Intake Slots -->
        <line x1="112" y1="32" x2="112" y2="66" stroke="var(--vfd-metal-900)" stroke-width="2" stroke-linecap="round" />
        <line x1="108" y1="30" x2="108" y2="68" stroke="var(--vfd-metal-900)" stroke-width="1.5" stroke-linecap="round"
          opacity="0.7" />

        <!-- Fan Housing Interior Window Aperture -->
        <circle cx="102" cy="49" r="14" fill="var(--vfd-metal-900)" stroke="var(--vfd-metal-800)" stroke-width="1" />

        <!-- Dynamic High-Speed 6-Blade Cooling Fan (高速旋转叶片) -->
        <g :transform="`translate(102, 49) rotate(${motorAngle})`">
          <!-- 6 Curved Aerodynamic Blades -->
          <path d="M 0 0 C -2 -7 2 -12 0 -13 C -2 -12 -5 -7 0 0 Z" :fill="boolValue ? activeColor : 'var(--vfd-metal-500)'" />
          <path d="M 0 0 C 6 -4 10 -6 11 -7 C 10 -9 5 -6 0 0 Z" :fill="boolValue ? activeColor : 'var(--vfd-metal-500)'" />
          <path d="M 0 0 C 7 2 11 6 12 7 C 10 9 6 6 0 0 Z" :fill="boolValue ? activeColor : 'var(--vfd-metal-500)'" />
          <path d="M 0 0 C 2 7 -2 12 0 13 C 2 12 5 7 0 0 Z" :fill="boolValue ? activeColor : 'var(--vfd-metal-500)'" />
          <path d="M 0 0 C -6 4 -10 6 -11 7 C -10 9 -5 6 0 0 Z" :fill="boolValue ? activeColor : 'var(--vfd-metal-500)'" />
          <path d="M 0 0 C -7 -2 -11 -6 -12 -7 C -10 -9 -6 -6 0 0 Z" :fill="boolValue ? activeColor : 'var(--vfd-metal-500)'" />

          <!-- Center Hub Nose Cone -->
          <circle cx="0" cy="0" r="3.2" fill="var(--vfd-metal-700)" stroke="var(--vfd-metal-500)" stroke-width="0.75" />
          <circle cx="0" cy="0" r="1.5" :fill="boolValue ? 'var(--vfd-metal-100)' : 'var(--vfd-metal-400)'" />
        </g>
      </g>
    </svg>
</template>
