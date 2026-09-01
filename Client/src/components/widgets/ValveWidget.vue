<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const valveAngle = computed(() => boolValue.value ? 0 : 90);
</script>

<template>
<svg width="100%" height="100%" viewBox="0 0 80 80"
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
</template>
