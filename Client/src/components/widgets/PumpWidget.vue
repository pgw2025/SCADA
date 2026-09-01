<script setup lang="ts">
import { defineProps, computed } from 'vue';
import { useWidgetBase } from './useWidgetBase';
import type { HmiWidgetProps } from './useWidgetBase';

const props = defineProps<HmiWidgetProps>();
const base = useWidgetBase(props);
const { isLockedControl, numValue, boolValue, normalizedPercent, defDefaults, propOr, activeColor, inactiveColor, strokeColor, fillColor, minValue, maxValue, unit, thresholdMin, thresholdMax, fontSize, align, bold, showBorder, showBackground, showInnerLabel, onText, offText, qualityBad, hasExplicitThresholdMax, hasExplicitThresholdMin, isHighAlert, isLowAlert, alertColor, width, height, ticks, timeString } = base;

const pumpAngle = computed(() =>
  boolValue.value ? (ticks.value * (12 + Math.min(36, Math.abs(numValue.value) / 4))) % 360 : 0
);
</script>

<template>
<svg width="100%" height="100%" viewBox="0 0 80 80"
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
</template>
